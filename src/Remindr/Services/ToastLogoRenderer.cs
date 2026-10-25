using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Remindr.Services;

/// <summary>Draws the round toast picture: a built-in picture on a soft disc, or an uploaded avatar filling the circle. UI thread / STA only.</summary>
public static class ToastLogoRenderer
{
    /// <summary>PNG bytes (96x96, transparent outside the circle) for a stored picture value, or null when it cannot be loaded.</summary>
    public static byte[]? Render(string value, string? avatarDirectory = null)
    {
        const int size = ToastImageCache.PixelSize;
        var uploaded = value.StartsWith(AvatarCatalog.FilePrefix, StringComparison.Ordinal);
        var source = AvatarCatalog.Load(value, avatarDirectory, size * 2);
        if (source is null) return null;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var centre = new Point(size / 2.0, size / 2.0);
        using (var dc = visual.RenderOpen())
        {
            if (uploaded)
            {
                dc.PushClip(new EllipseGeometry(centre, size / 2.0, size / 2.0));
                dc.DrawImage(source, new Rect(0, 0, size, size));
                dc.Pop();
            }
            else
            {
                // A neutral translucent disc reads on both the light and the dark toast; the picture sits inside it.
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x38, 0x80, 0x80, 0x80)), null, centre, size / 2.0, size / 2.0);
                const double inset = 14;
                dc.DrawImage(source, new Rect(inset, inset, size - 2 * inset, size - 2 * inset));
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
