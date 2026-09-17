using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Embedded Kestrel host shared by the native iOS app (APNs-woken fetch) and
/// the Shortcuts client (polling): both talk to the same REST endpoints.
///
/// Content travels as raw bytes, never Base64/JSON — metadata (type/fileName/
/// mimeType) rides in headers on upload and in the small /clipboard/meta
/// response on download, so a client (Shortcuts included) can fetch
/// /clipboard/data and get a natively-typed result (Image, File, Text) based
/// on the Content-Type header, with no manual encoding step on either side.
/// </summary>
internal sealed class ClipSyncServer
{
    private const string AuthHeader = "X-ClipSync-Key";
    private const string ContentTypeHeader = "X-Content-Type";
    private const string FileNameHeader = "X-File-Name";
    private const string ExtensionHeader = "X-Extension";

    // The sender only tells us the file extension (locale-proof — Shortcuts'
    // human-readable "type" names get translated, extensions never do); we
    // build the actual file name and guess a MIME type from it here.
    private static readonly Dictionary<string, string> MimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["heic"] = "image/heic",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["pdf"] = "application/pdf",
        ["txt"] = "text/plain",
        ["zip"] = "application/zip",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    private readonly AppConfig _config;
    private readonly ClipboardStore _store;
    private readonly DeviceStore _devices;
    private readonly List<WebSocket> _sockets = new();
    private readonly object _socketsLock = new();
    private WebApplication? _app;

    public event EventHandler<ClipboardPayload>? RemoteClipboardReceived;

    public ClipSyncServer(AppConfig config, ClipboardStore store, DeviceStore devices)
    {
        _config = config;
        _store = store;
        _devices = devices;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_config.Port}");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseWebSockets();

        app.Use(async (context, next) =>
        {
            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("unauthorized");
                return;
            }

            await next();
        });

        app.MapPost("/clipboard", async context =>
        {
            var contentType = context.Request.Headers[ContentTypeHeader].ToString();

            using var bodyStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(bodyStream);
            var bytes = bodyStream.ToArray();

            if (bytes.Length == 0)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            ClipboardPayload payload;

            if (contentType == "file")
            {
                var extension = context.Request.Headers[ExtensionHeader].ToString().TrimStart('.');
                var providedName = context.Request.Headers[FileNameHeader].ToString();
                var fileName = providedName.Length > 0
                    ? providedName
                    : extension.Length > 0 ? $"clipboard.{extension}" : "clipboard";
                var mimeType = extension.Length > 0 && MimeTypesByExtension.TryGetValue(extension, out var mime)
                    ? mime
                    : "application/octet-stream";

                payload = ClipboardPayload.ForFile(fileName, mimeType, bytes);
            }
            else
            {
                payload = ClipboardPayload.ForText(Encoding.UTF8.GetString(bytes));
            }

            _store.Set(payload);
            RemoteClipboardReceived?.Invoke(this, payload);
            await BroadcastMetaAsync(payload);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.MapGet("/clipboard/meta", async context =>
        {
            var payload = _store.GetLatest();
            await context.Response.WriteAsJsonAsync(ToMetaDto(payload));
        });

        app.MapGet("/clipboard/data", async context =>
        {
            var payload = _store.GetLatest();

            if (payload.Type == ClipboardContentType.Text)
            {
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(payload.Text ?? string.Empty);
                return;
            }

            context.Response.ContentType = payload.MimeType ?? "application/octet-stream";

            // fileName is NOT echoed back as a header here — clients read it from
            // the JSON /clipboard/meta response instead. Raw HTTP header values
            // can't carry non-ASCII text, and Windows file names often do
            // (e.g. a Japanese-locale default like "新規 文字文件.txt"), which
            // would otherwise crash this response while writing headers.
            await context.Response.Body.WriteAsync(payload.Data ?? []);
        });

        app.MapPost("/devices/register", async context =>
        {
            var registration = await context.Request.ReadFromJsonAsync<DeviceRegistration>();

            if (string.IsNullOrWhiteSpace(registration?.DeviceToken))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            _devices.Register(registration.DeviceToken);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();

            lock (_socketsLock)
            {
                _sockets.Add(socket);
            }

            await HoldOpenUntilClosedAsync(socket);
        });

        _app = app;
        await app.StartAsync();
    }

    private bool IsAuthorized(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(AuthHeader, out var value))
        {
            return false;
        }

        return string.Equals(value.ToString(), _config.SharedSecret, StringComparison.Ordinal);
    }

    private static ClipboardMetaDto ToMetaDto(ClipboardPayload payload) => new(
        payload.Type == ClipboardContentType.File ? "file" : "text",
        payload.FileName,
        payload.MimeType,
        payload.UpdatedAt);

    /// <summary>
    /// Windows-side sockets exist purely as a "something changed" push
    /// channel — a connected client re-fetches /clipboard/meta and
    /// /clipboard/data itself rather than receiving content inline here.
    /// </summary>
    private async Task HoldOpenUntilClosedAsync(WebSocket socket)
    {
        var buffer = new byte[1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
            }
        }
        catch (WebSocketException)
        {
            // Connection dropped; fall through to cleanup below.
        }
        finally
        {
            lock (_socketsLock)
            {
                _sockets.Remove(socket);
            }
        }
    }

    public async Task BroadcastMetaAsync(ClipboardPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ToMetaDto(payload));
        List<WebSocket> targets;

        lock (_socketsLock)
        {
            targets = _sockets.Where(s => s.State == WebSocketState.Open).ToList();
        }

        foreach (var socket in targets)
        {
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Ignore broken sockets; the next receive loop iteration will clean them up.
            }
        }
    }

    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
        }
    }

    private sealed record DeviceRegistration(string DeviceToken);
}

internal sealed record ClipboardMetaDto(string Type, string? FileName, string? MimeType, DateTimeOffset UpdatedAt);
