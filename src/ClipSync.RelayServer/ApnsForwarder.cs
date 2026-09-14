using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipSync.RelayServer;

/// <summary>
/// Signs and sends the actual silent push to Apple. This is the only place
/// the .p8 key is ever read — it never leaves this process.
/// </summary>
internal sealed class ApnsForwarder
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestVersion = new Version(2, 0),
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
    };

    private readonly RelayConfig _config;
    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAt = DateTimeOffset.MinValue;

    public ApnsForwarder(RelayConfig config)
    {
        _config = config;
    }

    public async Task<bool> SendSilentPushAsync(string deviceToken)
    {
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
