using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Remindr.Setup;

/// <summary>Follows the Windows app theme (AppsUseLightTheme) and accent colour. Read once at startup.</summary>
public static class ThemeSetup
{
    public static bool IsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return !(k?.GetValue("AppsUseLightTheme") is int v && v == 0);
        }
        catch { return true; }
    }

    public static Color ReadAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int abgr)
                return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }
        catch { /* fall through */ }
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    public static void Apply(Application app)
    {
        var light = IsLight();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Remindr.Setup;component/Styles/Glass." + (light ? "Light" : "Dark") + ".xaml")
        });

        var accent = ReadAccent();
        if (!light) accent = Blend(accent, Colors.White, 0.22);          // lighter accent on dark, like WPF-UI
        else accent = Blend(accent, Colors.Black, 0.08);
        var onAccent = Luminance(accent) > 0.55 ? Color.FromRgb(0x10, 0x10, 0x10) : Colors.White;

        app.Resources["SetupAccentColor"] = accent;
        app.Resources["SetupAccentBrush"] = Frozen(accent);
        app.Resources["SetupOnAccentBrush"] = Frozen(onAccent);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}
