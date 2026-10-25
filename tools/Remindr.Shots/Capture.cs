using System.Drawing;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Windows;
using System.Windows.Media;

namespace Remindr.Shots;

/// <summary>A captured picture: premultiplied-free BGRA32, top-down.</summary>
internal sealed class Frame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public Frame(int w, int h, byte[]? data = null) { Width = w; Height = h; Bgra = data ?? new byte[w * h * 4]; }

    public static Frame FromBitmap(Bitmap bmp)
    {
        var f = new Frame(bmp.Width, bmp.Height);
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, f.Bgra, y * bmp.Width * 4, bmp.Width * 4);
        }
        finally { bmp.UnlockBits(d); }
        return f;
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var d = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(Bgra, y * Width * 4, d.Scan0 + y * d.Stride, Width * 4);
        }
        finally { bmp.UnlockBits(d); }
        return bmp;
    }

    /// <summary>Opaque alpha everywhere, then transparent rounded corners (Windows 11 window corners are 8 px at 100%).</summary>
    public void RoundCorners(double radius)
    {
        for (var i = 3; i < Bgra.Length; i += 4) Bgra[i] = 255;
        void Corner(int cx, int cy, int x0, int y0)
        {
            var r = (int)Math.Ceiling(radius) + 1;
            for (var dy = 0; dy < r; dy++)
                for (var dx = 0; dx < r; dx++)
                {
                    var x = x0 + dx * (cx < Width / 2 ? 1 : -1);
                    var y = y0 + dy * (cy < Height / 2 ? 1 : -1);
                    // Pixel centre distance from the arc centre.
                    var px = radius - (dx + 0.5);
                    var py = radius - (dy + 0.5);
                    if (px <= 0 || py <= 0) continue;     // outside the corner square of the arc: fully inside the window
                    var dist = Math.Sqrt(px * px + py * py);
                    var cover = Math.Clamp(radius + 0.5 - dist, 0, 1);
                    Bgra[(y * Width + x) * 4 + 3] = (byte)Math.Round(255 * cover);
                }
        }
        Corner(0, 0, 0, 0);
        Corner(Width, 0, Width - 1, 0);
        Corner(0, Height, 0, Height - 1);
        Corner(Width, Height, Width - 1, Height - 1);
    }

    public Frame Crop(int x, int y, int w, int h)
    {
        var f = new Frame(w, h);
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(Bgra, ((y + row) * Width + x) * 4, f.Bgra, row * w * 4, w * 4);
        return f;
    }

    public Frame Scaled(int w, int h)
    {
        using var src = ToBitmap();
        using var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
        }
        return FromBitmap(dst);
    }

    /// <summary>True when every pixel is (nearly) the same as in <paramref name="other"/>.</summary>
    public double MeanDiff(Frame other)
    {
        if (other.Width != Width || other.Height != Height) return double.MaxValue;
        long sum = 0;
        for (var i = 0; i < Bgra.Length; i += 16)
            sum += Math.Abs(Bgra[i] - other.Bgra[i]) + Math.Abs(Bgra[i + 1] - other.Bgra[i + 1]) + Math.Abs(Bgra[i + 2] - other.Bgra[i + 2]);
        return sum / (Bgra.Length / 16.0);
    }
}

/// <summary>
/// A full-screen soft-gradient window kept behind the app while shooting, so Acrylic never shows the real desktop.
/// Runs on its own STA thread; the tool never captures anything outside the app window's rectangle.
/// </summary>
internal sealed class Backdrop : IDisposable
{
    private Thread? _thread;
    private System.Windows.Window? _win;
    private readonly ManualResetEventSlim _ready = new();
    public IntPtr Hwnd { get; private set; }

    public static (int X, int Y, int W, int H) Screen()
    {
        var w = Native.GetSystemMetrics(0); var h = Native.GetSystemMetrics(1);
        return (0, 0, w, h);
    }

    public void Start(bool dark)
    {
        _thread = new Thread(() =>
        {
            var (_, _, sw, sh) = Screen();
            var grad = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1) };
            if (dark)
            {
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x14, 0x16, 0x2E), 0));
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x24, 0x1E, 0x4A), 0.55));
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x1B, 0x2A, 0x4A), 1));
            }
            else
            {
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0xE4, 0xE6, 0xFF), 0));
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0xF1, 0xE4, 0xFB), 0.5));
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0xDD, 0xEE, 0xFB), 1));
            }
            _win = new System.Windows.Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                Topmost = true, Background = grad, Title = "Remindr Shots backdrop", AllowsTransparency = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, Width = 400, Height = 300
            };
            _win.SourceInitialized += (_, _) =>
            {
                Hwnd = new System.Windows.Interop.WindowInteropHelper(_win).Handle;
                Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, 0, 0, sw, sh, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                _ready.Set();
            };
            _win.Show();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(10));
        Thread.Sleep(400);
    }

    public void Dispose()
    {
        try { _win?.Dispatcher.Invoke(() => _win.Close()); _win?.Dispatcher.InvokeShutdown(); } catch (Exception) { /* closing */ }
    }
}

/// <summary>Sizing, raising and grabbing the app window.</summary>
internal static class Shooter
{
    public const int LogicalW = 1280, LogicalH = 800;

    /// <summary>Makes the window exactly 1280x800 logical pixels (scaled by the monitor DPI), centred, topmost and in front of the backdrop.</summary>
    public static void Place(AppWindow app, int logicalW = LogicalW, int logicalH = LogicalH)
    {
        var dpi = Native.GetDpiForWindow(app.Hwnd);
        var scale = dpi / 96.0;
        int pw = (int)Math.Round(logicalW * scale), ph = (int)Math.Round(logicalH * scale);
        var (_, _, sw, sh) = Backdrop.Screen();
        int x = Math.Max(0, (sw - pw) / 2), y = Math.Max(0, (sh - ph) / 2);
        for (var i = 0; i < 3; i++)
        {
            Native.SetWindowPos(app.Hwnd, Native.HWND_TOPMOST, x, y, pw, ph, Native.SWP_SHOWWINDOW);
            Thread.Sleep(150);
            Native.GetWindowRect(app.Hwnd, out var outer);
            var frame = Native.FrameBounds(app.Hwnd);
            // If the visible frame differs from the outer rect (invisible resize borders), correct the outer rect.
            var dx = (outer.Width - frame.Width); var dy = (outer.Height - frame.Height);
            if (dx == 0 && dy == 0 && frame.Width == pw && frame.Height == ph) break;
            pw += dx; ph += dy;
        }
        Native.SetForegroundWindow(app.Hwnd);
    }

    public static void Raise(AppWindow app)
    {
        Native.SetWindowPos(app.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | Native.SWP_NOACTIVATE); // NOSIZE | NOMOVE
        Native.SetForegroundWindow(app.Hwnd);
    }

    /// <summary>Grabs the window's visible rectangle from the screen (so Acrylic is included) and returns it at logical size.</summary>
    public static Frame Grab(AppWindow app, bool rounded = true, int? outW = null, int? outH = null)
    {
        var r = Native.FrameBounds(app.Hwnd);
        var scale = Native.GetDpiForWindow(app.Hwnd) / 96.0;
        using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(r.Width, r.Height));
        var f = Frame.FromBitmap(bmp);
        var tw = outW ?? (scale > 1.01 ? LogicalW : f.Width);
        var th = outH ?? (scale > 1.01 ? LogicalH : f.Height);
        if (tw != f.Width || th != f.Height) f = f.Scaled(tw, th);
        if (rounded) f.RoundCorners(8);
        return f;
    }

    /// <summary>Grabs every ~120 ms until three consecutive frames agree (the picture is at rest), then returns the last. For pages with a never-ending loop, tolerance can be raised.</summary>
    public static Frame GrabSteady(AppWindow app, bool rounded = true, int timeoutMs = 9000, double eps = 0.2)
    {
        Raise(app);
        Thread.Sleep(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a = Grab(app, false);
        var steady = 0;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(110);
            var b = Grab(app, false);
            steady = b.MeanDiff(a) < eps ? steady + 1 : 0;
            a = b;
            if (steady >= 3) break;
        }
        if (rounded) a.RoundCorners(8);
        return a;
    }

    /// <summary>Waits until two grabs 120 ms apart agree (animations finished), then returns the last one.</summary>
    public static Frame GrabStable(AppWindow app, bool rounded = true, int timeoutMs = 4000, double eps = 0.25)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Raise(app);
        Thread.Sleep(450);
        var prev = Grab(app, false);
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(150);
            var cur = Grab(app, false);
            if (cur.MeanDiff(prev) < eps) { prev = cur; break; }
            prev = cur;
        }
        if (rounded) prev.RoundCorners(8);
        return prev;
    }
}


