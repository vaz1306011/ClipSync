using ClipSync.RelayServer;

var config = RelayConfig.FromEnvironment();
var forwarder = new ApnsForwarder(config);

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "ClipSync relay is running.");

app.MapPost("/push", async (HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("X-Relay-Key", out var providedKey) ||
        !string.Equals(providedKey.ToString(), config.RelayKey, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    var body = await context.Request.ReadFromJsonAsync<PushRequest>();

    if (string.IsNullOrWhiteSpace(body?.DeviceToken))
    {
        return Results.BadRequest();
    }

    var succeeded = await forwarder.SendSilentPushAsync(body.DeviceToken);
    return succeeded ? Results.NoContent() : Results.StatusCode(StatusCodes.Status502BadGateway);
});

app.Run();

internal sealed record PushRequest(string DeviceToken);
