using ClipSync.WindowsApp.Server;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace ClipSync.WindowsApp;

/// <summary>
/// The light/dark colors shared by the app's WPF windows. Each window merges
/// the theme dictionary first, since there is no WPF Application to hold it.
/// </summary>
internal static class WindowTheme
{
    private static readonly string ThemePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipSync", "theme.txt");

    public static ApplicationTheme Resolve(AppConfig config) =>
        config.DarkTheme is { } dark
            ? (dark ? ApplicationTheme.Dark : ApplicationTheme.Light)
            : ReadTheme();

    public static void Apply(ResourceDictionary resources, ApplicationTheme theme)
    {
        resources.MergedDictionaries[0] = new ThemesDictionary { Theme = theme };

        bool isDark = theme == ApplicationTheme.Dark;
        resources["SettingsBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(31, 31, 31)
            : System.Windows.Media.Color.FromRgb(247, 247, 247));
        resources["SettingsCardBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(42, 42, 42)
            : System.Windows.Media.Colors.White);
        resources["SettingsCardBorderBrush"] = new System.Windows.Media.SolidColorBrush(isDark
            ? System.Windows.Media.Color.FromRgb(63, 63, 63)
            : System.Windows.Media.Color.FromRgb(225, 225, 225));
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
}
