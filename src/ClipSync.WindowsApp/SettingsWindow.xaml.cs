using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClipSync.WindowsApp;

public partial class SettingsWindow : FluentWindow
{
    private readonly Action _showPairingQrCode;

    public SettingsWindow(Action showPairingQrCode)
    {
        _showPairingQrCode = showPairingQrCode;
        InitializeComponent();
        ApplicationThemeManager.Apply(this);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ShowPairingQrCode_Click(object sender, RoutedEventArgs e)
    {
        _showPairingQrCode();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
