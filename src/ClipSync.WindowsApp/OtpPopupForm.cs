using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClipSync.WindowsApp;

/// <summary>
/// A floating button next to the caret that types the code when clicked. It
/// never takes focus, so the keystrokes land where the user was typing. Shown
/// only while that window is in front, and gone after a minute.
/// </summary>
internal sealed class OtpPopupForm : Form
{
    private const int WsExTopMost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;

    // Layout in 96 DPI pixels.
    private const int BaseWidth = 220;
    private const int BaseHeight = 56;

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeMethods.Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private readonly string _code;
    private readonly TextInputTarget _target;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly long _expiresAt;
    private float _scale = 1;

    private OtpPopupForm(string code, TextInputTarget target, int seconds)
    {
        _expiresAt = Environment.TickCount64 + Math.Clamp(seconds, 1, 3600) * 1000L;
        _code = code;
        _target = target;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(0, 122, 255);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        _timer.Tick += (_, _) => FollowTargetWindow();
    }

    public static OtpPopupForm ShowFor(string code, TextInputTarget target, int seconds)
    {
        using var _ = NativeMethods.UsePhysicalPixels();

        var form = new OtpPopupForm(code, target, seconds);
        form.PlaceAndShow();
        return form;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= WsExTopMost | WsExToolWindow | WsExNoActivate;
            return createParams;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseActivate)
        {
            m.Result = MaNoActivate;
            return;
        }

        base.WndProc(ref m);
    }

    private void PlaceAndShow()
    {
        var anchor = _target.Anchor;

        // Move onto the target's monitor first, so the DPI read below is that monitor's.
        SetWindowPos(Handle, IntPtr.Zero, anchor.Left, anchor.Bottom, 1, 1, SwpNoActivate);
        _scale = GetDpiForWindow(Handle) / 96f;

        var width = Scaled(BaseWidth);
        var height = Scaled(BaseHeight);
        var gap = Scaled(6);
        var x = anchor.Left;
        var y = anchor.Bottom + gap;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (GetMonitorInfo(MonitorFromPoint(anchor.Location, MonitorDefaultToNearest), ref info))
        {
            // Flip above the caret when there's no room below it.
            if (y + height > info.Work.Bottom)
            {
                y = anchor.Top - gap - height;
            }

            x = Math.Max(info.Work.Left, Math.Min(x, info.Work.Right - width));
        }

        using (var path = RoundedRectangle(new Rectangle(0, 0, width, height), Scaled(10)))
        {
            Region = new Region(path);
        }

        Show();
        SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, SwpNoActivate);
        _timer.Start();
    }

    private void FollowTargetWindow()
    {
        if (Environment.TickCount64 >= _expiresAt)
        {
            Close();
            return;
        }

        Visible = NativeMethods.GetForegroundWindow() == _target.Window;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // Right click dismisses without typing.
        if (e.Button == MouseButtons.Left && NativeMethods.GetForegroundWindow() == _target.Window)
        {
            KeyboardTyper.Type(_code);
        }

        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Speech bubble: an ellipse with a tail at the bottom left.
        graphics.FillEllipse(Brushes.White, Scaled(14), Scaled(15), Scaled(28), Scaled(23));
        graphics.FillPolygon(Brushes.White, new[]
        {
            new Point(Scaled(18), Scaled(30)),
            new Point(Scaled(15), Scaled(42)),
            new Point(Scaled(28), Scaled(36))
        });

        using var titleFont = new Font("Yu Gothic UI", Scaled(16), FontStyle.Bold, GraphicsUnit.Pixel);
        using var sourceFont = new Font("Yu Gothic UI", Scaled(12), FontStyle.Regular, GraphicsUnit.Pixel);

        TextRenderer.DrawText(graphics, $"コードを入力 {_code}", titleFont,
            new Point(Scaled(52), Scaled(7)), Color.White, BackColor);
        TextRenderer.DrawText(graphics, "SMS から", sourceFont,
            new Point(Scaled(52), Scaled(31)), Color.White, BackColor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Scaled(int value) => (int)Math.Round(value * _scale);

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
