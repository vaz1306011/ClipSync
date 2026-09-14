using System.Security.Cryptography;
using System.Text.Json;

namespace ClipSync.WindowsApp.Server;

internal sealed class AppConfig
{
    public int Port { get; set; } = 8787;
    public string SharedSecret { get; set; } = string.Empty;

    // APNs settings — fill these in manually after generating the .p8 key in
    // the Apple Developer portal. Push notifications stay disabled until all
    // four are set.
    public string ApnsKeyId { get; set; } = string.Empty;
    public string ApnsTeamId { get; set; } = string.Empty;
    public string ApnsBundleId { get; set; } = string.Empty;
    public string ApnsPrivateKeyPath { get; set; } = string.Empty;
    public bool ApnsUseSandbox { get; set; } = true;

    // If set, push wake-ups go through this relay (e.g. your NAS) instead of
    // straight to Apple, so the .p8 never has to leave the relay's machine.
    // Takes priority over the direct Apns* settings above when both are present.
    public string? RelayUrl { get; set; }
    public string? RelayKey { get; set; }

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipSync",
        "config.json");

    public static AppConfig LoadOrCreate()
    {
        var path = ConfigPath;

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json);

            if (loaded is not null && !string.IsNullOrEmpty(loaded.SharedSecret))
            {
                return loaded;
            }
        }

        var config = new AppConfig
        {
            Port = 8787,
            SharedSecret = GenerateSecret()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        return config;
    }

    private static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
