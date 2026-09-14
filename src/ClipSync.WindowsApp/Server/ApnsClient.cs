using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Sends silent ("content-available") pushes to APNs using the .p8 auth key —
/// these carry no clipboard content, they just wake the iOS app so it can pull
/// the latest content from this same server over the LAN.
/// </summary>
internal sealed class ApnsClient : IPushSender
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestVersion = new Version(2, 0),
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
    };

    private readonly AppConfig _config;
    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAt = DateTimeOffset.MinValue;

    public ApnsClient(AppConfig config)
    {
        _config = config;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.ApnsKeyId) &&
        !string.IsNullOrWhiteSpace(_config.ApnsTeamId) &&
        !string.IsNullOrWhiteSpace(_config.ApnsBundleId) &&
        File.Exists(_config.ApnsPrivateKeyPath);

    public async Task<bool> SendSilentPushAsync(string deviceToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var host = _config.ApnsUseSandbox
            ? "https://api.sandbox.push.apple.com"
            : "https://api.push.apple.com";

        var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{deviceToken}")
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Content = new StringContent("""{"aps":{"content-available":1}}""", Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", GetOrCreateJwt());
        request.Headers.Add("apns-topic", _config.ApnsBundleId);
        request.Headers.Add("apns-push-type", "background");
        request.Headers.Add("apns-priority", "5");

        using var response = await Http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private string GetOrCreateJwt()
    {
        // Apple asks providers not to mint a new token per request; reuse one for under an hour.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresAt)
        {
            return _cachedToken;
        }

        var issuedAt = DateTimeOffset.UtcNow;

        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            kid = _config.ApnsKeyId
        }));

        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _config.ApnsTeamId,
            iat = issuedAt.ToUnixTimeSeconds()
        }));

        var unsignedToken = $"{header}.{payload}";

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(_config.ApnsPrivateKeyPath));

        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(unsignedToken), HashAlgorithmName.SHA256);
        var jwt = $"{unsignedToken}.{Base64UrlEncode(signature)}";

        _cachedToken = jwt;
        _cachedTokenExpiresAt = issuedAt.AddMinutes(50);

        return jwt;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
