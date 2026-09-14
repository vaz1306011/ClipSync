using System.Net.Http.Json;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Forwards wake-up requests to a relay server (e.g. hosted on your own NAS)
/// that holds the real .p8 key, so this app can be handed to other people
/// without shipping your Apple push credential with it.
/// </summary>
internal sealed class RelayPushClient : IPushSender
{
    private const string RelayKeyHeader = "X-Relay-Key";

    private static readonly HttpClient Http = new();

    private readonly AppConfig _config;

    public RelayPushClient(AppConfig config)
    {
        _config = config;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.RelayUrl) &&
        !string.IsNullOrWhiteSpace(_config.RelayKey);

    public async Task<bool> SendSilentPushAsync(string deviceToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_config.RelayUrl!.TrimEnd('/')}/push")
        {
            Content = JsonContent.Create(new { deviceToken })
        };
        request.Headers.Add(RelayKeyHeader, _config.RelayKey);

        using var response = await Http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }
}
