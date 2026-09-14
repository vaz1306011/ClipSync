using System.Runtime.InteropServices;

namespace ClipSync.WindowsApp;

/// <summary>
/// Monitors the Windows clipboard via a hidden message-only window, so it works
/// without any visible UI (AddClipboardFormatListener requires a real HWND).
/// </summary>
internal sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private bool _suppressNext;

    public event EventHandler<string>? ClipboardTextChanged;

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

        string text;
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            text = Clipboard.GetText();
        }
        catch (ExternalException)
        {
            // Clipboard was locked by another process at the moment of the event; skip it.
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ClipboardTextChanged?.Invoke(this, text);
    }

    /// <summary>
    /// Writes text to the clipboard without re-triggering ClipboardTextChanged,
    /// so content synced in from another device doesn't get echoed back out.
    /// </summary>
    public void SetClipboardTextWithoutNotifying(string text)
    {
        _suppressNext = true;

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            _suppressNext = false;
        }
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }
}
