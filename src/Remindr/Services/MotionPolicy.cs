using System.Windows;

namespace Remindr.Services;

/// <summary>
/// Start-up switches for motion and effects, kept free of Application so they can be unit tested on plain ResourceDictionaries.
/// </summary>
public static class MotionPolicy
{
    /// <summary>Keys in Styles/Animations.xaml that are durations (zeroed under reduced motion).</summary>
    public static bool IsDurationKey(string key) => key.StartsWith("Dur", StringComparison.Ordinal);

    /// <summary>Keys in Styles/Animations.xaml that are stagger delays in milliseconds (zeroed under reduced motion).</summary>
    public static bool IsStaggerKey(string key) => key.StartsWith("Stagger", StringComparison.Ordinal);

    /// <summary>
    /// Reduced motion: every Dur* and Stagger* resource found in <paramref name="sources"/> (searched through merged
    /// dictionaries) becomes 0 in <paramref name="target"/>, and MotionEnabled becomes false. Found by name, so a new token
    /// is covered without touching this method.
    /// </summary>
    public static void ApplyReducedMotion(ResourceDictionary target, IEnumerable<ResourceDictionary> sources)
    {
        foreach (var source in sources) ZeroIn(target, source);
        target["MotionEnabled"] = false;
    }

    private static void ZeroIn(ResourceDictionary target, ResourceDictionary source)
    {
        foreach (var merged in source.MergedDictionaries) ZeroIn(target, merged);
        foreach (var key in source.Keys.OfType<string>())
        {
            if (IsDurationKey(key) && source[key] is Duration) target[key] = new Duration(TimeSpan.Zero);
            else if (IsStaggerKey(key) && source[key] is double) target[key] = 0.0;
        }
    }

    /// <summary>The Debug-only --no-effects switch: turns the second half of the effects gate off. Release builds ignore it.</summary>
    public static void ApplyLaunchArgs(ResourceDictionary target, LaunchArgs args, bool debugBuild)
    {
        if (debugBuild && args.NoEffects) target["VisualEffectsEnabled"] = false;
    }
}
