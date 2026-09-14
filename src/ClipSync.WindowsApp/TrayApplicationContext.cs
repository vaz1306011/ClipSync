using ClipSync.WindowsApp.Server;

namespace ClipSync.WindowsApp;

/// <summary>
/// Runs ClipSync with no main window: a tray icon plus the embedded server
/// and clipboard watcher, wired together so a change on either side syncs the other.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ClipboardWatcher _clipboardWatcher;
    private readonly ClipSyncServer _server;
    private readonly AppConfig _config;
    private readonly SynchronizationContext _uiContext;

    public TrayApplicationContext()
    {
        // Clipboard access must happen on this STA UI thread; the server's handlers
        // run on ASP.NET Core's thread pool, so remote updates get marshalled back here.
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _config = AppConfig.LoadOrCreate();

        var store = new ClipboardStore();

        _clipboardWatcher = new ClipboardWatcher();
        _clipboardWatcher.ClipboardTextChanged += OnLocalClipboardChanged;

        _server = new ClipSyncServer(_config, store);
        _server.RemoteClipboardReceived += OnRemoteClipboardReceived;
        _ = _server.StartAsync();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = $"ClipSync (port {_config.Port})"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"監聽埠: {_config.Port}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("複製配對金鑰", null, (_, _) => CopyPairingKey()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("結束", null, (_, _) => ExitApplication()));

        _trayIcon.ContextMenuStrip = menu;
    }

    private void OnLocalClipboardChanged(object? sender, string text)
    {
        _ = _server.BroadcastAsync(text);
    }

    private void OnRemoteClipboardReceived(object? sender, string text)
    {
        // Fired from the server's async request handling, not the UI thread.
        _uiContext.Post(_ => _clipboardWatcher.SetClipboardTextWithoutNotifying(text), null);
    }

    private void CopyPairingKey()
    {
        _clipboardWatcher.SetClipboardTextWithoutNotifying(_config.SharedSecret);
        _trayIcon.ShowBalloonTip(2000, "ClipSync", "配對金鑰已複製到剪貼板", ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        _clipboardWatcher.Dispose();
        _ = _server.StopAsync();
        Application.Exit();
    }
}
