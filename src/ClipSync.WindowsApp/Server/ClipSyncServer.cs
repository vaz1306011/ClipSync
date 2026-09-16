using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Embedded Kestrel host shared by the native iOS app (APNs-woken fetch) and
/// the Shortcuts client (polling): both talk to the same REST endpoints.
/// Windows itself stays connected over the WebSocket for instant push.
/// </summary>
internal sealed class ClipSyncServer
{
    private const string AuthHeader = "X-ClipSync-Key";

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
            var dto = await context.Request.ReadFromJsonAsync<ClipboardDto>();

            if (dto is null || (dto.Type == "text" && string.IsNullOrEmpty(dto.Text)) ||
                (dto.Type == "file" && (dto.Data is null || dto.Data.Length == 0)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var payload = dto.ToPayload();
            _store.Set(payload);
            RemoteClipboardReceived?.Invoke(this, payload);
            await BroadcastAsync(payload);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.MapGet("/clipboard/latest", async context =>
        {
            var payload = _store.GetLatest();
            await context.Response.WriteAsJsonAsync(ClipboardDto.From(payload));
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

            await ReceiveLoopAsync(socket);
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

    private async Task ReceiveLoopAsync(WebSocket socket)
    {
        var buffer = new byte[8192];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var dto = JsonSerializer.Deserialize<ClipboardDto>(messageStream.ToArray());

                if (dto is null)
                {
                    continue;
                }

                var payload = dto.ToPayload();
                _store.Set(payload);
                RemoteClipboardReceived?.Invoke(this, payload);
                await BroadcastAsync(payload, exclude: socket);
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

    public async Task BroadcastAsync(ClipboardPayload payload, WebSocket? exclude = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ClipboardDto.From(payload));
        List<WebSocket> targets;

        lock (_socketsLock)
        {
            targets = _sockets.Where(s => s.State == WebSocketState.Open && s != exclude).ToList();
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
