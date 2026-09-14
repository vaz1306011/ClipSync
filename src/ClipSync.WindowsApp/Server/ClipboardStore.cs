namespace ClipSync.WindowsApp.Server;

internal sealed class ClipboardStore
{
    private readonly object _lock = new();
    private string _latestContent = string.Empty;
    private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;

    public void Set(string text)
    {
        lock (_lock)
        {
            _latestContent = text;
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    public (string Content, DateTimeOffset UpdatedAt) GetLatest()
    {
        lock (_lock)
        {
            return (_latestContent, _updatedAt);
        }
    }
}
