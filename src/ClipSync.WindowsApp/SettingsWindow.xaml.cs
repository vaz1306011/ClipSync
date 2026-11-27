using ClipSync.WindowsApp.Server;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ClipSync.WindowsApp;

internal sealed record ConnectionStatus(bool ServerFailed, bool Connected, ClientContact? LastContact);

public partial class SettingsWindow : FluentWindow
{
    private static readonly string ThemePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipSync", "theme.txt");

    private readonly Action _showPairingQrCode;
    private readonly Func<AppConfig, Task> _applySettings;
    private readonly Func<ConnectionStatus> _getConnectionStatus;
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppConfig _savedConfig;
    private string _sharedKey;
    private object?[] _savedValues;
    private bool _saving;
    private bool _allowClose;

    internal SettingsWindow(AppConfig config, Func<AppConfig, Task> applySettings, Action showPairingQrCode,
        Func<ConnectionStatus> getConnectionStatus)
    {
        _showPairingQrCode = showPairingQrCode;
        _applySettings = applySettings;
        _getConnectionStatus = getConnectionStatus;
        _savedConfig = config;
        _sharedKey = config.SharedSecret;
        InitializeComponent();
        var theme = (config.DarkTheme ?? (ReadTheme() == ApplicationTheme.Dark))
            ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ShowValues(config);
        UpdateKeyDisplay();
        ApplyTheme(theme);
        ThemePicker.SelectedIndex = theme == ApplicationTheme.Dark ? 1 : 0;
        ThemePicker.SelectionChanged += ThemePicker_SelectionChanged;

        _savedValues = CaptureSettingsValues();
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnSettingsEdited));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnSettingsEdited));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnSettingsEdited));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnSettingsEdited));

        UpdateConnectionStatus();
        _connectionTimer.Tick += (_, _) => UpdateConnectionStatus();
        _connectionTimer.Start();
        Closed += (_, _) => _connectionTimer.Stop();
    }

    private void UpdateConnectionStatus()
    {
        var status = _getConnectionStatus();
        var (color, text, detail) = status switch
        {
            { ServerFailed: true } => (System.Windows.Media.Brushes.Firebrick, "サーバーが起動していません",
                "「接続」でポート番号を確認してください。"),
            { Connected: true } => (System.Windows.Media.Brushes.ForestGreen, "接続中",
                status.LastContact is { } contact ? $"端末の IP アドレス: {contact.Address}" : ""),
            { LastContact: { } contact } => (System.Windows.Media.Brushes.Gray, "未接続",
                $"最後の通信: {contact.At:M/d HH:mm:ss}（{contact.Address}）"),
            _ => (System.Windows.Media.Brushes.Gray, "未接続",
                "iPhone でショートカットを実行すると、ここに接続状態が表示されます。")
        };

        ConnectionDot.Fill = color;
        ConnectionStateText.Text = text;
        ConnectionDetailText.Text = detail;
    }

    private void ShowValues(AppConfig config)
    {
        StartOnSignInBox.IsChecked = config.StartOnSignIn;
        DoubleClickActionPicker.SelectedIndex = Math.Clamp(config.TrayDoubleClickAction, 0, 1);
        SyncPausedBox.IsChecked = config.SyncPaused || !config.SyncEnabled;
        SyncTextBox.IsChecked = config.SyncText;
        SyncImagesBox.IsChecked = config.SyncImages;
        SyncFilesBox.IsChecked = config.SyncFiles;
        IgnoreLocalFilesBox.IsChecked = config.IgnoreLocalFiles;
        CopyOtpAutomaticallyBox.IsChecked = config.CopyOtpAutomatically;
        DetectOtpBox.IsChecked = config.DetectOtp;
        NormalizeOtpBox.IsChecked = config.NormalizeOtp;
        ExcludeOtpUrlsBox.IsChecked = config.ExcludeOtpUrls;
        NotificationModePicker.SelectedIndex = Math.Clamp(config.OtpNotificationMode, 0, 2);
        PopupSecondsBox.Text = config.OtpPopupSeconds.ToString();
        FileLimitBox.Text = config.FileLimitMb.ToString();
        PortBox.Text = config.Port.ToString();
        KeywordsBox.Text = config.OtpKeywords;
        PatternBox.Text = config.OtpPattern;
    }

    private object?[] CaptureSettingsValues() =>
    [
        ThemePicker.SelectedIndex, StartOnSignInBox.IsChecked, SyncPausedBox.IsChecked,
        SyncTextBox.IsChecked, SyncImagesBox.IsChecked, SyncFilesBox.IsChecked,
        FileLimitBox.Text.Trim(), IgnoreLocalFilesBox.IsChecked, NotificationModePicker.SelectedIndex,
        CopyOtpAutomaticallyBox.IsChecked, PopupSecondsBox.Text.Trim(), DetectOtpBox.IsChecked,
        KeywordsBox.Text, PatternBox.Text.Trim(), NormalizeOtpBox.IsChecked, ExcludeOtpUrlsBox.IsChecked,
        PortBox.Text.Trim(), _sharedKey, DoubleClickActionPicker.SelectedIndex
    ];

    private void OnSettingsEdited(object sender, RoutedEventArgs e) => UpdateApplyButton();

    private bool HasChanges => !CaptureSettingsValues().SequenceEqual(_savedValues);

    private void UpdateApplyButton() => ApplyButton.IsEnabled = HasChanges;

    private static ApplicationTheme ReadTheme()
    {
        try
        {
            return File.Exists(ThemePath) && File.ReadAllText(ThemePath).Trim() == nameof(ApplicationTheme.Dark)
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ApplicationTheme.Light;
        }
    }

    private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = ThemePicker.SelectedIndex == 1 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplyTheme(theme);
    }

    private void ApplyTheme(ApplicationTheme theme)
    {
        Resources.MergedDictionaries[0] = new ThemesDictionary { Theme = theme };

        bool isDark = theme == ApplicationTheme.Dark;
        Resources["SettingsCheckBorderBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(166, 166, 166)
            : System.Windows.Media.Color.FromRgb(120, 120, 120));
        Resources["SettingsCheckHoverBrush"] = isDark
            ? System.Windows.Media.Brushes.White
            : System.Windows.Media.Brushes.Black;
        Resources["SettingsBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(31, 31, 31)
            : System.Windows.Media.Color.FromRgb(247, 247, 247));
        Resources["SettingsCardBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(42, 42, 42)
            : System.Windows.Media.Colors.White);
        Resources["SettingsCardBorderBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(63, 63, 63)
            : System.Windows.Media.Color.FromRgb(225, 225, 225));
    }

    private void ShowPairingQrCode_Click(object sender, RoutedEventArgs e)
    {
        if (_sharedKey != _savedConfig.SharedSecret || PortBox.Text.Trim() != _savedConfig.Port.ToString())
        {
            ShowNotice("新しい接続情報で QR コードを作るには、先に「適用」を押してください。");
            return;
        }

        _showPairingQrCode();
    }

    private void ShowKey_Click(object sender, RoutedEventArgs e) => UpdateKeyDisplay();

    private void UpdateKeyDisplay()
    {
        bool show = ShowKeyBox.IsChecked == true;
        MaskedKeyBox.Password = _sharedKey;
        VisibleKeyBox.Text = show ? _sharedKey : "";
        MaskedKeyBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        VisibleKeyBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RegenerateKey_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, "適用すると、接続済みの端末は再ペアリングが必要になります。共有キーを再生成しますか？",
                Title, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            _sharedKey = AppConfig.GenerateSecret();
            UpdateKeyDisplay();
            UpdateApplyButton();
        }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, "ポート番号を含むすべての設定を初期値に戻しますか？共有キーは変わりません。",
                Title, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        // Only fills in the window, so Cancel still backs out. The shared key
        // stays: it identifies the paired devices and has no default.
        var defaults = new AppConfig();
        ShowValues(defaults);
        ThemePicker.SelectedIndex = defaults.DarkTheme == true ? 1 : 0;
        UpdateApplyButton();
    }

    private void TestPattern_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PatternBox.Text))
        {
            ShowNotice("追加の正規表現を入力してください。");
            return;
        }

        try
        {
            OtpExtractor.ValidatePatterns(PatternBox.Text);
            var config = _savedConfig.Copy();
            config.NormalizeOtp = NormalizeOtpBox.IsChecked == true;
            config.ExcludeOtpUrls = ExcludeOtpUrlsBox.IsChecked == true;
            var text = OtpExtractor.Prepare(PatternSampleBox.Text, config);

            var skipped = "";
            var result = OtpExtractor.ExtractByRules(text, PatternBox.Text,
                (line, ex) => skipped += $"\n{line} 行目は処理に時間がかかりすぎるため飛ばされます。");
            ShowNotice((result is { } found
                ? $"{found.Line} 行目のルールを使用\n一致: {found.Match.Value}\n認証コード: {found.Code}"
                : "どのルールからも認証コードを取り出せないため、標準の検出が使われます。") + skipped);
        }
        catch (ArgumentException ex)
        {
            ShowNotice(ex.Message);
        }
    }

    private AppConfig ReadSettings()
    {
        // Start from the file, so hand-edited settings this window doesn't show
        // (APNs, relay) survive even if they haven't been reloaded.
        var next = AppConfig.LoadOrCreate();
        next.StartOnSignIn = StartOnSignInBox.IsChecked == true;
        next.TrayDoubleClickAction = DoubleClickActionPicker.SelectedIndex;
        next.SyncPaused = SyncPausedBox.IsChecked == true;
        next.SyncEnabled = true;
        next.SyncText = SyncTextBox.IsChecked == true;
        next.SyncImages = SyncImagesBox.IsChecked == true;
        next.SyncFiles = SyncFilesBox.IsChecked == true;
        next.IgnoreLocalFiles = IgnoreLocalFilesBox.IsChecked == true;
        next.CopyOtpAutomatically = CopyOtpAutomaticallyBox.IsChecked == true;
        next.DetectOtp = DetectOtpBox.IsChecked == true;
        next.NormalizeOtp = NormalizeOtpBox.IsChecked == true;
        next.ExcludeOtpUrls = ExcludeOtpUrlsBox.IsChecked == true;
        next.DarkTheme = ThemePicker.SelectedIndex == 1;
        next.OtpNotificationMode = NotificationModePicker.SelectedIndex;
        next.OtpPopupSeconds = ReadNumber(PopupSecondsBox.Text, 1, 3600, "コードボタンの表示時間");
        next.FileLimitMb = ReadNumber(FileLimitBox.Text, 0, int.MaxValue, "サイズ上限");
        next.Port = ReadNumber(PortBox.Text, 1, 65535, "ポート番号");
        next.OtpKeywords = KeywordsBox.Text;
        next.OtpPattern = PatternBox.Text.Trim();
        OtpExtractor.ValidatePatterns(PatternBox.Text);
        next.SharedSecret = _sharedKey;
        return next;
    }

    private static int ReadNumber(string input, int min, int max, string label)
    {
        if (!int.TryParse(input, out int value) || value < min || value > max)
            throw new ArgumentException($"{label}には {min}〜{max} の整数を入力してください。");
        return value;
    }

    private async Task<bool> SaveAsync()
    {
        if (_saving)
            return false;
        if (!HasChanges)
            return true;

        _saving = true;
        try
        {
            var next = ReadSettings();
            ((FrameworkElement)Content).IsEnabled = false;
            await _applySettings(next);
            _savedConfig = next;
            _savedValues = CaptureSettingsValues();
            UpdateApplyButton();
            return true;
        }
        catch (Exception ex)
        {
            ShowNotice($"設定を保存できませんでした。\n{ex.Message}");
            return false;
        }
        finally
        {
            ((FrameworkElement)Content).IsEnabled = true;
            _saving = false;
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveAsync())
        {
            CloseWithoutSaving();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWithoutSaving();

    // Close, unlike DialogResult, is safe if the window is already gone, which
    // happens when the app exits while a save is in progress.
    internal void CloseWithoutSaving()
    {
        _allowClose = true;
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _allowClose)
            return;

        if (_saving)
        {
            e.Cancel = true;
            return;
        }

        if (!HasChanges)
            return;

        var answer = System.Windows.MessageBox.Show(this, "変更を保存しますか？", Title,
            System.Windows.MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.No)
            return;

        e.Cancel = true;
        if (answer != MessageBoxResult.Yes)
            return;

        await Task.Yield();
        if (await SaveAsync())
        {
            _allowClose = true;
            Close();
        }
    }

    private void ShowNotice(string message) => System.Windows.MessageBox.Show(this, message, Title);
}
