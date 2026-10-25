using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace Remindr.Services;

/// <summary>
/// The notification-area icons: the app icon, and the same icon with an alert dot (something is overdue or being nagged). Made in memory
/// from the one app icon (full colour, so it reads on a light and on a dark taskbar) instead of shipping four extra .ico files.
/// </summary>
public static class TrayIcons
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Same red as the Important badge, so the dot means the same thing everywhere.</summary>
    public static readonly Color AlertColor = Color.FromArgb(0xD1, 0x34, 0x38);

    /// <summary>The app icon, with a dot in the bottom corner when <paramref name="alert"/> is set. Never throws; null if the icon cannot be read.</summary>
    public static Icon? Create(Stream appIcon, bool alert)
    {
        try
        {
            using var icon = new Icon(appIcon);
            if (!alert) return (Icon)icon.Clone();
            using var bitmap = icon.ToBitmap();
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawDot(g, bitmap.Width, bitmap.Height);
            }
            var handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Making the tray icon");
            return null;
        }
    }

    /// <summary>The dot geometry for an icon of the given size: a filled circle about 40% of the width with a thin light ring, bottom-trailing corner.</summary>
    public static RectangleF DotBounds(int width, int height)
    {
        var d = Math.Max(6f, width * 0.42f);
        return new RectangleF(width - d - 2f, height - d - 2f, d, d);
    }

    private static void DrawDot(Graphics g, int width, int height)
    {
        var r = DotBounds(width, height);
        using var ring = new SolidBrush(Color.White);
        using var fill = new SolidBrush(AlertColor);
        var pad = Math.Max(1f, width / 16f);
        g.FillEllipse(ring, r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad);
        g.FillEllipse(fill, r);
    }
}
