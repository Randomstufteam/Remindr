using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Remindr.Converters;

public static class ColorUtil
{
    public static Color Parse(string? hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex ?? "#3B82F6"); }
        catch { return Color.FromRgb(0x3B, 0x82, 0xF6); }
    }

    public static SolidColorBrush Brush(string? hex)
    {
        var b = new SolidColorBrush(Parse(hex));
        b.Freeze();
        return b;
    }

    private static readonly SolidColorBrush DarkText = Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush LightText = Frozen(Colors.White);

    /// <summary>WCAG relative luminance (0 = black, 1 = white).</summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>
    /// Text colour for content drawn on <paramref name="hex"/>. White unless it would fall below 3:1
    /// (the WCAG minimum for large/bold text), e.g. amber, orange, emerald, teal; then near-black.
    /// </summary>
    public static SolidColorBrush ForegroundFor(string? hex) =>
        1.05 / (Luminance(Parse(hex)) + 0.05) >= 3.0 ? LightText : DarkText;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ColorUtil.Brush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
