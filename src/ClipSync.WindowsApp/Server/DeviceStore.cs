using System.Text.Json;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Registered iOS device tokens, used later to target APNs silent-push wake-ups.
/// Persisted to disk so a restart of the Windows app doesn't forget already-paired
/// phones and silently stop pushing to them until they happen to re-register.
/// </summary>
internal sealed class DeviceStore
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipSync",
        "devices.json");

    private readonly object _lock = new();
    private readonly HashSet<string> _deviceTokens;

    public DeviceStore()
    {
        _deviceTokens = new HashSet<string>(Load(), StringComparer.OrdinalIgnoreCase);
    }

    public void Register(string deviceToken)
    {
        lock (_lock)
        {
            if (!_deviceTokens.Add(deviceToken))
            {
                return;
            }

            Save(_deviceTokens);
        }
    }

    public IReadOnlyCollection<string> GetAll()
    {
        lock (_lock)
        {
            return _deviceTokens.ToList();
        }
    }

    private static IEnumerable<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    private static void Save(IEnumerable<string> tokens)
    {
        var path = StorePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(tokens.ToArray()));
    }
}
