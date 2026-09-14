namespace ClipSync.WindowsApp.Server;

internal sealed class ClipboardStore
{
    private readonly object _lock = new();
    private string _latestContent = string.Empty;
    private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;

    /// <summary>Fired after every Set, regardless of whether the change came from
    /// the local clipboard watcher or a remote client — the single point where
    /// APNs push-notify-all hooks in.</summary>
    public event EventHandler<string>? Updated;

    public void Set(string text)
    {
        lock (_lock)
        {
            _latestContent = text;
            _updatedAt = DateTimeOffset.UtcNow;
        }

        Updated?.Invoke(this, text);
    }

    public (string Content, DateTimeOffset UpdatedAt) GetLatest()
    {
        lock (_lock)
        {
            return (_latestContent, _updatedAt);
        }
    }
}
