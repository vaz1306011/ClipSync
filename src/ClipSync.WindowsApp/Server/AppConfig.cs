using System.Security.Cryptography;
using System.Text.Json;

namespace ClipSync.WindowsApp.Server;

internal sealed class AppConfig
{
    public int Port { get; set; } = 913;
    public string SharedSecret { get; set; } = string.Empty;

    public bool? DarkTheme { get; set; }
    public bool StartOnSignIn { get; set; }
    public bool SyncPaused { get; set; }
    public bool SyncEnabled { get; set; } = true;
    public bool SyncText { get; set; } = true;
    public bool SyncImages { get; set; } = true;
    public bool SyncFiles { get; set; } = true;
    public int FileLimitMb { get; set; }
    public bool IgnoreLocalFiles { get; set; }
    public int OtpNotificationMode { get; set; } = 1;
    public bool CopyOtpAutomatically { get; set; }
    public int OtpPopupSeconds { get; set; } = 60;
    public bool DetectOtp { get; set; } = true;
    public string OtpKeywords { get; set; } = OtpExtractor.DefaultKeywords;
    public string OtpPattern { get; set; } = "";
    public bool NormalizeOtp { get; set; } = true;
    public bool ExcludeOtpUrls { get; set; } = true;

    public AppConfig Copy() => (AppConfig)MemberwiseClone();

    public bool AllowsClipboard(ClipboardPayload payload, bool local = false)
    {
        if (!SyncEnabled || SyncPaused)
            return false;
        if (payload.Type == ClipboardContentType.Text)
            return SyncText;

        bool image = payload.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
        return (image ? SyncImages : SyncFiles && !(local && IgnoreLocalFiles))
            && (FileLimitMb == 0 || (payload.Data?.LongLength ?? 0) <= FileLimitMb * 1024L * 1024L);
    }

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
            Port = 913,
            SharedSecret = GenerateSecret()
        };

        config.Save();
        return config;
    }

    public void Save()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
