using System.Net.WebSockets;
using System.Text;
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
    private readonly List<WebSocket> _sockets = new();
    private readonly object _socketsLock = new();
    private WebApplication? _app;

    public event EventHandler<string>? RemoteClipboardReceived;

    public ClipSyncServer(AppConfig config, ClipboardStore store)
    {
        _config = config;
        _store = store;
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
            using var reader = new StreamReader(context.Request.Body);
            var text = await reader.ReadToEndAsync();

            if (string.IsNullOrEmpty(text))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            _store.Set(text);
            RemoteClipboardReceived?.Invoke(this, text);
            await BroadcastAsync(text);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.MapGet("/clipboard/latest", async context =>
        {
            var (content, updatedAt) = _store.GetLatest();
            await context.Response.WriteAsJsonAsync(new { content, updatedAt });
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
        var buffer = new byte[4096];

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

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                _store.Set(text);
                RemoteClipboardReceived?.Invoke(this, text);
                await BroadcastAsync(text, exclude: socket);
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

    public async Task BroadcastAsync(string text, WebSocket? exclude = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
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
}
