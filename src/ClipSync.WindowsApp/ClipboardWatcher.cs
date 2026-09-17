using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClipSync.WindowsApp.Server;

namespace ClipSync.WindowsApp;

/// <summary>
/// Monitors the Windows clipboard via a hidden message-only window, so it works
/// without any visible UI (AddClipboardFormatListener requires a real HWND).
/// Handles text, images, and single-file copies.
/// </summary>
internal sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private static readonly Dictionary<string, string> MimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".heic"] = "image/heic",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private bool _suppressNext;

    public event EventHandler<ClipboardPayload>? ClipboardChanged;

    public ClipboardWatcher()
    {
        CreateHandle(new CreateParams
        {
            Caption = "ClipSyncClipboardListener",
            Parent = HWND_MESSAGE
        });

        AddClipboardFormatListener(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE)
        {
            HandleClipboardUpdate();
        }

        base.WndProc(ref m);
    }

    private void HandleClipboardUpdate()
    {
        if (_suppressNext)
        {
            _suppressNext = false;
            return;
        }

        ClipboardPayload? payload;

        try
        {
            payload = ReadClipboard();
        }
        catch (ExternalException)
        {
            // Clipboard was locked by another process at the moment of the event; skip it.
            return;
        }

        if (payload is null)
        {
            return;
        }

        ClipboardChanged?.Invoke(this, payload);
    }

    private static ClipboardPayload? ReadClipboard()
    {
        // Check file/image before text: a copied file or screenshot often also
        // carries a text representation (e.g. its path), which isn't what we want.
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();

            if (files.Count == 0 || files[0] is not { } path || !File.Exists(path))
            {
                return null;
            }

            var fileName = Path.GetFileName(path);
            var mimeType = MimeTypesByExtension.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
            var data = File.ReadAllBytes(path);

            return ClipboardPayload.ForFile(fileName, mimeType, data);
        }

        if (Clipboard.ContainsImage())
        {
            using var image = Clipboard.GetImage();

            if (image is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);

            return ClipboardPayload.ForFile("clipboard-image.png", "image/png", stream.ToArray());
        }

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            return string.IsNullOrEmpty(text) ? null : ClipboardPayload.ForText(text);
        }

        return null;
    }

    /// <summary>
    /// Writes a payload to the clipboard without re-triggering ClipboardChanged,
    /// so content synced in from another device doesn't get echoed back out.
    /// </summary>
    public void SetClipboardPayloadWithoutNotifying(ClipboardPayload payload)
    {
        _suppressNext = true;

        try
        {
            if (payload.Type == ClipboardContentType.Text)
            {
                Clipboard.SetText(payload.Text ?? string.Empty);
                return;
            }

            var data = payload.Data ?? [];
            var fileName = payload.FileName ?? "file";

            // Always drop a real file — never try to paste as an inline bitmap.
            // Windows only exposes ONE clipboard "winner" when both an image and
            // a file-drop format are present (it prefers the bitmap and throws
            // away the file name), and on machines with extra codecs installed
            // (e.g. a HEIF extension) that silently swallows even formats we'd
            // want to keep as real, named files. Explorer-pasteable and
            // correctly named beats being directly pasteable as an image.
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllBytes(tempPath, data);

            var dropList = new StringCollection { tempPath };
            Clipboard.SetFileDropList(dropList);
        }
        catch (Exception)
        {
            // Malformed payload from a client (bad image bytes, clipboard locked by
            // another process, etc.) — never let this take down the whole app.
            _suppressNext = false;
        }
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }
}
