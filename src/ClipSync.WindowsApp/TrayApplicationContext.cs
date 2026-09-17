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
    private readonly ToolStripMenuItem _portMenuItem;
    private readonly SynchronizationContext _uiContext;

    private AppConfig _config;
    private ClipboardStore _store;
    private PushNotificationService _pushService;
    private ClipSyncServer _server;

    public TrayApplicationContext()
    {
        // Clipboard access must happen on this STA UI thread; the server's handlers
        // run on ASP.NET Core's thread pool, so remote updates get marshalled back here.
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _clipboardWatcher = new ClipboardWatcher();
        _clipboardWatcher.ClipboardChanged += OnLocalClipboardChanged;

        _config = AppConfig.LoadOrCreate();
        (_store, _pushService, _server) = StartServerPipeline(_config);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = $"ClipSync (port {_config.Port})"
        };

        _portMenuItem = new ToolStripMenuItem(GetDisplayAddressLabel()) { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_portMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("ペアリングキーをコピー", null, (_, _) => CopyPairingKey()));
        menu.Items.Add(new ToolStripMenuItem("ペアリング QR コードを表示", null, (_, _) => ShowPairingQrCode()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("設定を再読み込み", null, (_, _) => _ = ReloadConfigAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => ExitApplication()));

        _trayIcon.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Builds the store/push/server trio for a given config. Used both at
    /// startup and by "設定を再読み込み" — kept as one method so the two stay in sync.
    /// </summary>
    private (ClipboardStore Store, PushNotificationService PushService, ClipSyncServer Server) StartServerPipeline(AppConfig config)
    {
        var store = new ClipboardStore();

        var devices = new DeviceStore();
        IPushSender pushSender = string.IsNullOrWhiteSpace(config.RelayUrl)
            ? new ApnsClient(config)
            : new RelayPushClient(config);
        var pushService = new PushNotificationService(devices, pushSender);
        store.Updated += (_, _) => _ = pushService.NotifyAllAsync();

        var server = new ClipSyncServer(config, store, devices);
        server.RemoteClipboardReceived += OnRemoteClipboardReceived;
        _ = server.StartAsync();

        return (store, pushService, server);
    }

    private async Task ReloadConfigAsync()
    {
        await _server.StopAsync();

        _config = AppConfig.LoadOrCreate();
        (_store, _pushService, _server) = StartServerPipeline(_config);

        _trayIcon.Text = $"ClipSync (port {_config.Port})";
        _portMenuItem.Text = GetDisplayAddressLabel();
        _trayIcon.ShowBalloonTip(2000, "ClipSync", "設定を再読み込みしました", ToolTipIcon.Info);
    }

    private string GetDisplayAddressLabel()
    {
        var host = LocalNetworkInfo.GetLocalIPv4() ?? "不明";
        return $"リッスンアドレス: {host}:{_config.Port}";
    }

    private void OnLocalClipboardChanged(object? sender, ClipboardPayload payload)
    {
        _store.Set(payload);
        _ = _server.BroadcastMetaAsync(payload);
    }

    private void OnRemoteClipboardReceived(object? sender, ClipboardPayload payload)
    {
        // Fired from the server's async request handling, not the UI thread.
        _uiContext.Post(_ => _clipboardWatcher.SetClipboardPayloadWithoutNotifying(payload), null);
    }

    private void CopyPairingKey()
    {
        _clipboardWatcher.SetClipboardPayloadWithoutNotifying(ClipboardPayload.ForText(_config.SharedSecret));
        _trayIcon.ShowBalloonTip(2000, "ClipSync", "ペアリングキーをクリップボードにコピーしました", ToolTipIcon.Info);
    }

    private void ShowPairingQrCode()
    {
        var host = LocalNetworkInfo.GetLocalIPv4();

        if (host is null)
        {
            MessageBox.Show("ローカルネットワークの IP が見つかりません。Wi-Fi または有線ネットワークに接続されているか確認してください。", "ClipSync",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var serverUrl = $"http://{host}:{_config.Port}";
        var payload = PairingQrCode.BuildPayload(serverUrl, _config.SharedSecret);
        using var qrImage = PairingQrCode.GenerateImage(payload);

        using var form = new QrCodeForm(qrImage, payload);
        form.ShowDialog();
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        _clipboardWatcher.Dispose();
        _ = _server.StopAsync();
        Application.Exit();
    }
}
