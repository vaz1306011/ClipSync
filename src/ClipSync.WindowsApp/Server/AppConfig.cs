using System.Security.Cryptography;
using System.Text.Json;

namespace ClipSync.WindowsApp.Server;

internal sealed class AppConfig
{
    public int Port { get; set; } = 8787;
    public string SharedSecret { get; set; } = string.Empty;

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
