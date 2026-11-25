using System.Runtime.InteropServices;

namespace ClipSync.WindowsApp;

/// <summary>
/// Types text into whatever has keyboard focus. Sends Unicode characters rather
/// than virtual keys, so the keyboard layout and IME don't alter the result.
/// </summary>
internal static class KeyboardTyper
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    // MouseInput is never sent; it's the largest member, which sets the size
    // SendInput expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static void Type(string text)
    {
        var inputs = text
            .SelectMany(c => new[] { Key(c, KeyEventUnicode), Key(c, KeyEventUnicode | KeyEventKeyUp) })
            .ToArray();

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input Key(char c, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { ScanCode = c, Flags = flags } }
    };
}
