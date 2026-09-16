namespace ClipSync.WindowsApp.Server;

internal sealed class ClipboardStore
{
    private readonly object _lock = new();
    private ClipboardPayload _latest = ClipboardPayload.ForText(string.Empty);

    /// <summary>Fired after every Set, regardless of whether the change came from
    /// the local clipboard watcher or a remote client — the single point where
    /// APNs push-notify-all hooks in.</summary>
    public event EventHandler<ClipboardPayload>? Updated;

    public void Set(ClipboardPayload payload)
    {
        lock (_lock)
        {
            _latest = payload;
        }

        Updated?.Invoke(this, payload);
    }

    public ClipboardPayload GetLatest()
    {
        lock (_lock)
        {
            return _latest;
        }
    }
}
