using ClipSync.WindowsApp.Server;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ClipSync.WindowsApp;

public partial class SettingsWindow : FluentWindow
{
    private static readonly string ThemePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipSync", "theme.txt");

    private readonly Action _showPairingQrCode;
    private readonly Func<AppConfig, Task> _applySettings;
    private AppConfig _savedConfig;
    private string _sharedKey;
    private bool _saving;
    private bool _allowClose;

    internal SettingsWindow(AppConfig config, Func<AppConfig, Task> applySettings, Action showPairingQrCode)
    {
        _showPairingQrCode = showPairingQrCode;
        _applySettings = applySettings;
        _savedConfig = config;
        _sharedKey = config.SharedSecret;
        InitializeComponent();
        var theme = (config.DarkTheme ?? (ReadTheme() == ApplicationTheme.Dark))
            ? ApplicationTheme.Dark : ApplicationTheme.Light;
        StartOnSignInBox.IsChecked = config.StartOnSignIn;
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
        UpdateKeyDisplay();
        ApplyTheme(theme);
        ThemePicker.SelectedIndex = theme == ApplicationTheme.Dark ? 1 : 0;
        ThemePicker.SelectionChanged += ThemePicker_SelectionChanged;
    }

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
        }
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
            var config = _savedConfig.Copy();
            config.NormalizeOtp = NormalizeOtpBox.IsChecked == true;
            config.ExcludeOtpUrls = ExcludeOtpUrlsBox.IsChecked == true;
            var match = OtpExtractor.CreateCustomPattern(PatternBox.Text)
                .Match(OtpExtractor.Prepare(PatternSampleBox.Text, config));
            ShowNotice(match.Success ? $"一致: {match.Value}" : "一致する部分がありません。");
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            ShowNotice(ex.Message);
        }
    }

    private AppConfig ReadSettings()
    {
        var next = _savedConfig.Copy();
        next.StartOnSignIn = StartOnSignInBox.IsChecked == true;
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
        if (next.OtpPattern.Length != 0)
            OtpExtractor.CreateCustomPattern(next.OtpPattern);
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

        _saving = true;
        try
        {
            var next = ReadSettings();
            ((FrameworkElement)Content).IsEnabled = false;
            await _applySettings(next);
            _savedConfig = next;
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
            _allowClose = true;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = false;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _allowClose)
            return;

        e.Cancel = true;
        if (_saving)
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
