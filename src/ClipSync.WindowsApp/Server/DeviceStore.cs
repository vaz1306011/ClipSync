namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Registered iOS device tokens, used later to target APNs silent-push wake-ups.
/// </summary>
internal sealed class DeviceStore
{
    private readonly object _lock = new();
    private readonly HashSet<string> _deviceTokens = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string deviceToken)
    {
        lock (_lock)
        {
            _deviceTokens.Add(deviceToken);
        }
    }

    public IReadOnlyCollection<string> GetAll()
    {
        lock (_lock)
        {
            return _deviceTokens.ToList();
        }
    }
}
