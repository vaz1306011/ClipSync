using System.Runtime.InteropServices;
using System.Windows.Automation;
using Accessibility;

namespace ClipSync.WindowsApp;

/// <summary>Where the user is typing: the window, and the caret (or field) on screen.</summary>
internal sealed record TextInputTarget(IntPtr Window, Rectangle Anchor);

/// <summary>
/// Finds the text input the user is currently in, if any. No single API covers
/// every app, so three are tried: the Win32 system caret (classic controls),
/// the MSAA caret (Chromium and Electron apps) and UI Automation (the rest).
/// </summary>
internal static class TextInputLocator
{
    private const uint ObjIdCaret = 0xFFFFFFF8;

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public NativeMethods.Rect CaretRect;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr window, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object? accessible);

    /// <summary>
    /// Queries other processes, which may be slow or hung — call it off the UI thread.
    /// Returns null when no text input is found.
    /// </summary>
    public static TextInputTarget? Find()
    {
        using var _ = NativeMethods.UsePhysicalPixels();

        var window = NativeMethods.GetForegroundWindow();

        if (window == IntPtr.Zero)
        {
            return null;
        }

        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };

        if (!GetGUIThreadInfo(GetWindowThreadProcessId(window, IntPtr.Zero), ref info))
        {
            info = default;
        }

        var anchor = FindSystemCaret(info)
            ?? FindAccessibleCaret(info.Focus != IntPtr.Zero ? info.Focus : window)
            ?? FindFocusedField();

        return anchor is { } found ? new TextInputTarget(window, found) : null;
    }

    private static Rectangle? FindSystemCaret(GuiThreadInfo info)
    {
        var rect = info.CaretRect;

        if (info.Caret == IntPtr.Zero || rect.Bottom <= rect.Top)
        {
            return null;
        }

        var origin = new Point(rect.Left, rect.Top);

        return ClientToScreen(info.Caret, ref origin)
            ? new Rectangle(origin.X, origin.Y, Math.Max(1, rect.Right - rect.Left), rect.Bottom - rect.Top)
            : null;
    }

    private static Rectangle? FindAccessibleCaret(IntPtr window)
    {
        try
        {
            var interfaceId = typeof(IAccessible).GUID;

            if (AccessibleObjectFromWindow(window, ObjIdCaret, ref interfaceId, out var accessible) != 0
                || accessible is not IAccessible caret)
            {
                return null;
            }

            caret.accLocation(out var left, out var top, out var width, out var height, 0);

            // Apps without a caret answer with an empty rectangle.
            return height > 0 && (left != 0 || top != 0)
                ? new Rectangle(left, top, Math.Max(1, width), height)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Rectangle? FindFocusedField()
    {
        try
        {
            var element = AutomationElement.FocusedElement;

            if (element is null || !element.Current.IsEnabled || !IsEditable(element))
            {
                return null;
            }

            var bounds = element.Current.BoundingRectangle;

            return bounds.IsEmpty
                ? null
                : new Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsEditable(AutomationElement element)
    {
        var type = element.Current.ControlType;

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            // A web page itself is a read-only Document; lists and sliders also
            // have a value but aren't places to type.
            return (type == ControlType.Edit || type == ControlType.Document || type == ControlType.ComboBox)
                && !((ValuePattern)pattern).Current.IsReadOnly;
        }

        return type == ControlType.Edit;
    }
}
