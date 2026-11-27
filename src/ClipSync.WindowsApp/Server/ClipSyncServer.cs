using System.Net;
using System.Text.RegularExpressions;
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
    private const int MaxOtpBodyBytes = 64 * 1024;

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

    private volatile AppConfig _config;

    // The updatedAt last reported by /clipboard/meta.
    private DateTimeOffset _reportedUpdatedAt = DateTimeOffset.MinValue;
    private readonly object _reportedLock = new();

    public void UpdateConfig(AppConfig config)
    {
        var previous = _config;
        _config = config;
        if (previous.SharedSecret != config.SharedSecret)
        {
            _lastContact = null;
            DisconnectClients();
        }
    }

    // The shortcut polls every second or so while it runs, so a phone that has
    // been quiet for this long has stopped or is out of reach.
    private static readonly TimeSpan ContactTimeout = TimeSpan.FromSeconds(10);

    private volatile ClientContact? _lastContact;

    /// <summary>The last authorized request from another device, if any.</summary>
    public ClientContact? LastContact => _lastContact;

    /// <summary>Whether a device is syncing now: it holds a socket open or called recently.</summary>
    public bool IsClientConnected
    {
        get
        {
            lock (_socketsLock)
            {
                if (_sockets.Any(s => s.State == WebSocketState.Open))
                    return true;
            }

            return _lastContact is { } contact && DateTimeOffset.Now - contact.At < ContactTimeout;
        }
    }

    private void RecordContact(HttpContext context)
    {
        // Requests from this PC itself say nothing about the phone.
        if (context.Connection.RemoteIpAddress is not { } address || IPAddress.IsLoopback(address))
            return;

        _lastContact = new ClientContact(
            DateTimeOffset.Now, address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    private void DisconnectClients()
    {
        lock (_socketsLock)
        {
            foreach (var socket in _sockets)
                socket.Abort();
        }
    }

    private ClipboardPayload VisibleClipboard()
    {
        var payload = _store.GetLatest();
        return _config.AllowsClipboard(payload) ? payload : ClipboardStore.Empty;
    }

    // Clients download whenever updatedAt changes, so with nothing to hand out
    // (paused, disallowed, or cleared) it stays as last reported. Otherwise
    // they would overwrite their clipboard with an empty text.
    private ClipboardMetaDto VisibleMeta()
    {
        lock (_reportedLock)
        {
            var payload = VisibleClipboard();
            if (ReferenceEquals(payload, ClipboardStore.Empty))
                return ToMetaDto(payload) with { UpdatedAt = _reportedUpdatedAt };

            _reportedUpdatedAt = payload.UpdatedAt;
            return ToMetaDto(payload);
        }
    }

    private readonly ClipboardStore _store;
    private readonly DeviceStore _devices;
    private readonly List<WebSocket> _sockets = new();
    private readonly object _socketsLock = new();
    private readonly OtpInbox _otpInbox = new();
    private WebApplication? _app;

    public event EventHandler<ClipboardPayload>? RemoteClipboardReceived;

    /// <summary>Fired with the code extracted from an SMS posted to /otp.</summary>
    public event EventHandler<string>? OtpReceived;

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

            RecordContact(context);
            await next();
            // Again, so that a long transfer doesn't count as silence.
            if (IsAuthorized(context))
                RecordContact(context);
        });

        app.MapPost("/clipboard", async context =>
        {
            var config = _config;
            if (!config.SyncEnabled || config.SyncPaused)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            var contentType = context.Request.Headers[ContentTypeHeader].ToString();
            // HTML and RTF items may turn into text, which the limit doesn't
            // cover; those are checked after conversion.
            var extension = context.Request.Headers[ExtensionHeader].ToString().TrimStart('.');
            if (contentType == "file" && config.FileLimitMb > 0
                && !IsHtmlExtension(extension) && !RtfConverter.IsRtfExtension(extension)
                && context.Request.ContentLength > config.FileLimitMb * 1024L * 1024L)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            using var bodyStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(bodyStream);
            var bytes = bodyStream.ToArray();

            if (bytes.Length == 0)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            ClipboardPayload payload;

            // iOS reports a copied web address as an .html item, so the shortcut
            // uploads it as a file — either the bare URL or the fetched page.
            // Recover the address and treat it as text.
            if (contentType == "file" && IsHtmlExtension(extension) && TryGetUrlText(bytes) is { } urlText)
            {
                payload = ClipboardPayload.ForText(urlText);
            }
            // Text copied from iOS Notes arrives as an .rtf item; paste it as text.
            else if (contentType == "file" && RtfConverter.IsRtfExtension(extension)
                && RtfConverter.TryConvertToText(bytes) is { } rtfText)
            {
                payload = ClipboardPayload.ForText(rtfText);
            }
            else if (contentType == "file")
            {
                var providedName = context.Request.Headers[FileNameHeader].ToString();

                // iOS copies photos as HEIC, which most Windows apps can't paste.
                if (extension.Equals("heic", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals("heif", StringComparison.OrdinalIgnoreCase))
                {
                    var png = await HeicConverter.TryConvertToPngAsync(bytes);

                    if (png is not null)
                    {
                        bytes = png;
                        extension = "png";

                        if (providedName.Length > 0)
                        {
                            providedName = Path.ChangeExtension(providedName, "png");
                        }
                    }
                }

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

            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!_config.AllowsClipboard(payload))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            _store.Set(payload);
            RemoteClipboardReceived?.Invoke(this, payload);
            await BroadcastMetaAsync(payload);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        // Takes one or more whole message bodies (see OtpInbox). Only extracted
        // codes are passed on: messages aren't stored, and never enter the synced
        // clipboard.
        app.MapPost("/otp", async context =>
        {
            var config = _config;
            if (!config.DetectOtp)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            using var bodyStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(bodyStream);

            if (bodyStream.Length is 0 or > MaxOtpBodyBytes)
            {
                context.Response.StatusCode = bodyStream.Length == 0
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status413PayloadTooLarge;
                return;
            }

            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var body = Encoding.UTF8.GetString(bodyStream.ToArray());

            foreach (var code in _otpInbox.TakeNewCodes(body, DateTimeOffset.UtcNow, config))
            {
                OtpReceived?.Invoke(this, code);
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.MapGet("/clipboard/meta", async context =>
        {
            await context.Response.WriteAsJsonAsync(VisibleMeta());
        });

        app.MapGet("/clipboard/data", async context =>
        {
            var payload = VisibleClipboard();

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
                if (!IsAuthorized(context))
                {
                    socket.Abort();
                    return;
                }
                _sockets.Add(socket);
            }

            await HoldOpenUntilClosedAsync(socket);
        });

        _app = app;
        try
        {
            await app.StartAsync();
        }
        catch
        {
            _app = null;
            await app.DisposeAsync();
            throw;
        }
    }

    private static bool IsHtmlExtension(string extension) =>
        extension.Equals("html", StringComparison.OrdinalIgnoreCase)
        || extension.Equals("htm", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex PageUrlRegex = new(
        "<meta[^>]+property=[\"']og:url[\"'][^>]+content=[\"']([^\"']+)[\"']"
        + "|<link[^>]+rel=[\"']canonical[\"'][^>]+href=[\"']([^\"']+)[\"']",
        RegexOptions.IgnoreCase);

    private static string? TryGetUrlText(byte[] bytes)
    {
        if (bytes.Length > 512 * 1024)
        {
            return null;
        }

        if (bytes.Length > 4096 || Encoding.UTF8.GetString(bytes).TrimStart().StartsWith('<'))
        {
            var match = PageUrlRegex.Match(Encoding.UTF8.GetString(bytes));
            var pageUrl = WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);

            return Uri.TryCreate(pageUrl, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                ? pageUrl
                : null;
        }

        var text = Encoding.UTF8.GetString(bytes).Trim();

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !text.Any(char.IsWhiteSpace)
            ? text
            : null;
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
        if (!_config.AllowsClipboard(payload))
            return;

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
            DisconnectClients();
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private sealed record DeviceRegistration(string DeviceToken);
}

internal sealed record ClientContact(DateTimeOffset At, IPAddress Address);

internal sealed record ClipboardMetaDto(string Type, string? FileName, string? MimeType, DateTimeOffset UpdatedAt);
