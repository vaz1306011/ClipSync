namespace ClipSync.WindowsApp;

/// <summary>
/// Shown on demand from the tray menu — the app otherwise has no main window.
/// Scan this once in the iOS "setup" shortcut instead of typing the server
/// URL and pairing key by hand.
/// </summary>
internal sealed class QrCodeForm : Form
{
    public QrCodeForm(Bitmap qrImage, string payload)
    {
        Text = "ClipSync ペアリング QR コード";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 380);

        var pictureBox = new PictureBox
        {
            Image = qrImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 20),
            Size = new Size(280, 280)
        };

        var hintLabel = new Label
        {
            Text = "ショートカットの「セットアップ」でこの QR コードを\nスキャンすると、サーバー URL とペアリングキーが自動的に保存されます。",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 310),
            Size = new Size(300, 50)
        };

        Controls.Add(pictureBox);
        Controls.Add(hintLabel);
    }
}
