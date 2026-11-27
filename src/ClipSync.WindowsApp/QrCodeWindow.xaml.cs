using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClipSync.WindowsApp;

/// <summary>
/// Shown on demand from the tray menu or the settings window. Scan this once in
/// the iOS "setup" shortcut instead of typing the server URL and pairing key by
/// hand.
/// </summary>
public partial class QrCodeWindow : FluentWindow
{
    internal QrCodeWindow(Bitmap qrImage, ApplicationTheme theme)
    {
        InitializeComponent();
        WindowTheme.Apply(Resources, theme);
        QrImage.Source = ToImageSource(qrImage);
    }

    private static BitmapImage ToImageSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        // Reads everything now, so the stream can be closed.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        return image;
    }
}
