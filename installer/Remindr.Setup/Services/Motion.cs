using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Remindr.Setup;

/// <summary>Reduced-motion aware animation helpers. When Windows animations are off, everything lands instantly.</summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static Duration Dur(string key, double fallbackMs) =>
        Application.Current?.TryFindResource(key) is Duration d ? d : new Duration(TimeSpan.FromMilliseconds(fallbackMs));

    /// <summary>Page transition length (DurSlow, 250 ms; 0 under reduced motion).</summary>
    public static Duration Slow => Dur("DurSlow", 250);
    public static Duration Fast => Dur("DurFast", 150);

    public static double StaggerMs => Application.Current?.TryFindResource("StaggerMs") is double d ? d : 40;

    /// <summary>Fades and slides items up one after another.</summary>
    public static void Stagger(params UIElement[] items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var el = items[i];
            if (!Enabled) { el.Opacity = 1; continue; }
            var tt = new TranslateTransform(0, 10);
            el.RenderTransform = tt;
            el.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(80 + i * StaggerMs * 1.5);
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Slow) { BeginTime = begin, EasingFunction = EaseOut });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Slow) { BeginTime = begin, EasingFunction = EaseOut });
        }
    }
}
