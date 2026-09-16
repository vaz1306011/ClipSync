using System.Text.Json;
using QRCoder;

namespace ClipSync.WindowsApp;

internal static class PairingQrCode
{
    /// <summary>
    /// The Shortcuts setup shortcut scans this once: "Get Dictionary from Input"
    /// on the scanned text, then "Get Value for" url / key.
    /// </summary>
    public static string BuildPayload(string serverUrl, string pairingKey) =>
        JsonSerializer.Serialize(new { url = serverUrl, key = pairingKey });

    public static Bitmap GenerateImage(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new QRCode(data);
        return qrCode.GetGraphic(10);
    }
}
