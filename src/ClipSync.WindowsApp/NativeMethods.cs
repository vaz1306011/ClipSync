using System.Runtime.InteropServices;

namespace ClipSync.WindowsApp;

/// <summary>The user32 calls shared by caret detection, the code popup and typing.</summary>
internal static class NativeMethods
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    /// <summary>
    /// Switches this thread to physical screen pixels until disposed. The rest of
    /// the app stays system DPI aware, which its fixed-size dialogs rely on.
    /// </summary>
    public static IDisposable UsePhysicalPixels() =>
        new DpiScope(SetThreadDpiAwarenessContext(PerMonitorAwareV2));

    private sealed class DpiScope(IntPtr previous) : IDisposable
    {
        public void Dispose() => SetThreadDpiAwarenessContext(previous);
    }
}
