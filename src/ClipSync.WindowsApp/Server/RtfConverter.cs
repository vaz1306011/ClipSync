using System.Text;
using System.Windows.Forms;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Extracts plain text from RTF bytes. iOS hands text copied from Notes to the
/// shortcut as an .rtf item; the rich-text control is only used as an RTF
/// parser, on a dedicated STA thread because it needs one.
/// </summary>
internal static class RtfConverter
{
    public static bool IsRtfExtension(string extension) =>
        extension.Equals("rtf", StringComparison.OrdinalIgnoreCase);

    public static string? TryConvertToText(byte[] rtfBytes)
    {
        var rtf = Encoding.Latin1.GetString(rtfBytes);

        if (!rtf.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
        {
            return null;
        }

        string? text = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var box = new RichTextBox();
                box.Rtf = rtf;
                text = box.Text;
            }
            catch
            {
                text = null;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return string.IsNullOrEmpty(text) ? null : text.TrimEnd('\r', '\n', '\0');
    }
}
