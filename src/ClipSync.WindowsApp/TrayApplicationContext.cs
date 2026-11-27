using ClipSync.WindowsApp.Server;
using Microsoft.Win32;
using System.Reflection;
using Microsoft.Toolkit.Uwp.Notifications;

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

    private volatile AppConfig _config;
    private readonly SemaphoreSlim _settingsLock = new(1);
    private Task _serverStart;
    private SettingsWindow? _settingsWindow;
    private QrCodeWindow? _qrCodeWindow;
    private bool _exitRequested;
    private ClipboardStore _store;
    private PushNotificationService _pushService;
    private ClipSyncServer _server;

    // Toast argument carrying the code, so each entry in the notification
    // center copies its own one.
    private const string OtpArgument = "otp";

    private OtpPopupForm? _otpPopup;

    public TrayApplicationContext()
    {
        // Clipboard access must happen on this STA UI thread; the server's handlers
        // run on ASP.NET Core's thread pool, so remote updates get marshalled back here.
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _config = AppConfig.LoadOrCreate();
        _clipboardWatcher = new ClipboardWatcher(() => _config);
        _clipboardWatcher.ClipboardChanged += OnLocalClipboardChanged;

        (_store, _pushService, _server) = CreateServerPipeline(_config);
        _serverStart = _server.StartAsync();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = $"ClipSync (port {_config.Port})"
        };

        // Also delivers the click that launched the app, if it wasn't running.
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        _portMenuItem = new ToolStripMenuItem(GetDisplayAddressLabel()) { Enabled = false };
        _ = ReportStartFailureAsync(_serverStart, _config.Port);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_portMenuItem);
        menu.Items.Add(new ToolStripMenuItem("設定", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("ペアリング QR コードを表示", null, (_, _) => ShowPairingQrCode()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("設定を再読み込み", null, (_, _) => _ = ReloadConfigAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => ExitApplication()));

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseDoubleClick += OnTrayDoubleClick;
    }

    private void OnTrayDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_config.TrayDoubleClickAction == 1)
            ShowPairingQrCode();
        else
            ShowSettings();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        try
        {
            _settingsWindow = new SettingsWindow(_config, ApplySettingsAsync, ShowPairingQrCode, GetConnectionStatus);
            _settingsWindow.ShowDialog();
        }
        finally
        {
            _settingsWindow = null;
        }

        if (_exitRequested)
            ExitApplication();
    }

    // Reads the fields on every call: applying settings may replace the server.
    private ConnectionStatus GetConnectionStatus() =>
        new(_serverStart.IsFaulted, _server.IsClientConnected, _server.LastContact);

    /// <summary>
    /// Builds the store/push/server trio for a given config. Used both at
    /// startup and by "設定を再読み込み" — kept as one method so the two stay in sync.
    /// </summary>
    private (ClipboardStore Store, PushNotificationService PushService, ClipSyncServer Server) CreateServerPipeline(AppConfig config)
    {
        var store = new ClipboardStore();

        var devices = new DeviceStore();
        var pushService = new PushNotificationService(devices, CreatePushSender(config));
        store.Updated += (_, _) => _ = pushService.NotifyAllAsync();

        var server = new ClipSyncServer(config, store, devices);
        server.RemoteClipboardReceived += OnRemoteClipboardReceived;
        server.OtpReceived += OnOtpReceived;

        return (store, pushService, server);
    }

    private static IPushSender CreatePushSender(AppConfig config) =>
        string.IsNullOrWhiteSpace(config.RelayUrl) ? new ApnsClient(config) : new RelayPushClient(config);

    private async Task ReloadConfigAsync()
    {
        if (_settingsWindow is not null)
            return;

        try
        {
            await ApplyConfigAsync(AppConfig.LoadOrCreate(), save: false);
            _trayIcon.ShowBalloonTip(2000, "ClipSync", "設定を再読み込みしました", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ClipSync");
        }
    }

    private Task ApplySettingsAsync(AppConfig next) => ApplyConfigAsync(next, save: true);

    private async Task ApplyConfigAsync(AppConfig next, bool save)
    {
        await _settingsLock.WaitAsync();
        try
        {
            try { await _serverStart; }
            catch when (_serverStart.IsFaulted) { }

            if (next.Port != _config.Port)
            {
                var replacement = CreateServerPipeline(next);
                await replacement.Server.StartAsync();
                try
                {
                    if (save)
                        SaveSettings(next);
                }
                catch
                {
                    await replacement.Server.StopAsync();
                    throw;
                }

                var previous = _server;
                (_store, _pushService, _server) = replacement;
                _serverStart = Task.CompletedTask;
                _config = next;
                _ = previous.StopAsync();
            }
            else
            {
                if (save)
                    SaveSettings(next);
                _pushService.UpdateSender(CreatePushSender(next));
                _server.UpdateConfig(next);
                _store.ClearIfDisallowed(next);
                _config = next;

                // Retry a server that never started, without holding the other
                // settings back if the port is still taken.
                if (_serverStart.IsFaulted)
                {
                    (_store, _pushService, _server) = CreateServerPipeline(next);
                    _serverStart = _server.StartAsync();
                    _ = ReportStartFailureAsync(_serverStart, next.Port);
                }
            }

            _trayIcon.Text = $"ClipSync (port {_config.Port})";
            _portMenuItem.Text = GetDisplayAddressLabel();
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task ReportStartFailureAsync(Task start, int port)
    {
        try
        {
            await start;
        }
        catch (Exception ex)
        {
            _uiContext.Post(_ => _trayIcon.ShowBalloonTip(10000, "ClipSync",
                $"ポート {port} でサーバーを起動できませんでした。\n{ex.Message}", ToolTipIcon.Error), null);
        }
    }

    private static void SaveSettings(AppConfig config)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var previous = run.GetValue("ClipSync") as string;
        try
        {
            if (config.StartOnSignIn)
            {
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルが見つかりません。");
                var command = $"\"{executable}\"";
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    command += $" \"{Assembly.GetExecutingAssembly().Location}\"";
                run.SetValue("ClipSync", command);
            }
            else if (previous is not null)
            {
                run.DeleteValue("ClipSync");
            }

            config.Save();
        }
        catch
        {
            if (previous is null)
                run.DeleteValue("ClipSync", throwOnMissingValue: false);
            else
                run.SetValue("ClipSync", previous);
            throw;
        }
    }

    private string GetDisplayAddressLabel()
    {
        var host = LocalNetworkInfo.GetLocalIPv4() ?? "不明";
        return $"リッスンアドレス: {host}:{_config.Port}";
    }

    private void OnLocalClipboardChanged(object? sender, ClipboardPayload payload)
    {
        if (!_config.AllowsClipboard(payload, local: true))
            return;

        _store.Set(payload);
        _ = _server.BroadcastMetaAsync(payload);
    }

    private void OnRemoteClipboardReceived(object? sender, ClipboardPayload payload)
    {
        // Fired from the server's async request handling, not the UI thread.
        _uiContext.Post(_ =>
        {
            if (ReferenceEquals(sender, _server) && _config.AllowsClipboard(payload))
                _clipboardWatcher.SetClipboardPayloadWithoutNotifying(payload);
        }, null);
    }

    // Several codes can arrive in one request; announce them in order, so the
    // popup left on screen is the newest one's.
    private readonly object _otpNotificationsLock = new();
    private Task _otpNotifications = Task.CompletedTask;

    private void OnOtpReceived(object? sender, string code)
    {
        lock (_otpNotificationsLock)
        {
            _otpNotifications = _otpNotifications
                .ContinueWith(_ => NotifyOtpAsync(code), TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task NotifyOtpAsync(string code)
    {
        var config = _config;
        if (!config.DetectOtp)
            return;

        // Settings may be saved while this runs, so each step looks at the
        // current ones; the code is only dropped if detection was turned off.
        if (config.CopyOtpAutomatically)
            _uiContext.Post(_ =>
            {
                if (_config is { DetectOtp: true, CopyOtpAutomatically: true })
                    _clipboardWatcher.SetClipboardPayloadWithoutNotifying(ClipboardPayload.ForText(code));
            }, null);

        TextInputTarget? target = null;
        // Looking for the caret asks other apps, so don't wait on a hung one.
        if (config.OtpNotificationMode != 0)
        {
            var search = Task.Run(TextInputLocator.Find);
            target = await Task.WhenAny(search, Task.Delay(1500)) == search && search.IsCompletedSuccessfully
                ? search.Result
                : null;
        }

        if (target is not null)
        {
            _uiContext.Post(_ =>
            {
                var current = _config;
                if (!current.DetectOtp || current.OtpNotificationMode == 0)
                    return;

                _otpPopup?.Close();
                _otpPopup = OtpPopupForm.ShowFor(code, target, current.OtpPopupSeconds);
            }, null);
        }

        // A toast rather than a balloon tip: it stays in the Windows notification
        // center, and can still be clicked there. With the popup up it goes there
        // silently, as the record of the code.
        config = _config;
        if (!config.DetectOtp)
            return;

        new ToastContentBuilder()
            .AddArgument(OtpArgument, code)
            .AddText($"認証コード {code}")
            .AddText("クリックでコピー")
            .Show(toast => toast.SuppressPopup = config.OtpNotificationMode == 1 && target is not null);
    }

    // Copying only on click leaves the clipboard alone until the code is wanted.
    // The non-notifying write also keeps the code from syncing back to the phone.
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        if (!ToastArguments.Parse(e.Argument).TryGetValue(OtpArgument, out var code))
        {
            return;
        }

        // Fired on a background thread, not the UI thread.
        _uiContext.Post(_ => _clipboardWatcher.SetClipboardPayloadWithoutNotifying(ClipboardPayload.ForText(code)), null);
    }

    private void ShowPairingQrCode()
    {
        if (_qrCodeWindow is not null)
        {
            _qrCodeWindow.Activate();
            return;
        }

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

        var window = new QrCodeWindow(qrImage, _settingsWindow?.SelectedTheme ?? WindowTheme.Resolve(_config));
        if (_settingsWindow is { IsVisible: true })
        {
            window.Owner = _settingsWindow;
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        }

        try
        {
            _qrCodeWindow = window;
            window.ShowDialog();
        }
        finally
        {
            _qrCodeWindow = null;
        }

        // With settings still open, ShowSettings exits once its own loop ends.
        if (_exitRequested && _settingsWindow is null)
            ExitApplication();
    }

    private void ExitApplication()
    {
        // The WPF windows run their own modal loops: close them first, and
        // whoever opened them comes back here once the loop has ended.
        if (_settingsWindow is not null || _qrCodeWindow is not null)
        {
            _exitRequested = true;
            _qrCodeWindow?.Close();
            _settingsWindow?.CloseWithoutSaving();
            return;
        }

        _trayIcon.Visible = false;
        _clipboardWatcher.Dispose();
        _ = _server.StopAsync();
        Application.Exit();
    }
}
