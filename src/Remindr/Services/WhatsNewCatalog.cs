using System.Globalization;
using System.Text.RegularExpressions;
using Wpf.Ui.Controls;

namespace Remindr.Services;

/// <summary>
/// One highlight on the What's New page. <paramref name="TitleKey"/> / <paramref name="TextKey"/> are string-table keys;
/// <paramref name="Visual"/> is a built-in picture key (see <see cref="PictureCatalog"/>, e.g. "alarm_clock") or a Fluent
/// <see cref="SymbolRegular"/> name (e.g. "Flag24").
/// </summary>
public sealed record WhatsNewEntry(string TitleKey, string TextKey, string Visual);

/// <summary>What a <see cref="WhatsNewEntry.Visual"/> turned out to be.</summary>
public sealed record WhatsNewVisual(string? PictureKey, SymbolRegular? Symbol)
{
    /// <summary>The stored picture value (<c>builtin:key</c>) when this is a picture.</summary>
    public string? PictureValue => PictureKey is null ? null : PictureCatalog.BuiltinPrefix + PictureKey;
}

/// <summary>
/// The bundled highlights of each release, in the order they are shown. The text is in Strings.resx under
/// <c>WhatsNew_&lt;ver&gt;_&lt;n&gt;_Title / _Text</c> where &lt;ver&gt; is the version with underscores for dots (0.4.0 is 0_4_0, so 0.4.0 and 0.40.0 never share keys) and n counts from 1.
/// Other languages fall back to English until they are translated. Pure data, no UI.
/// </summary>
public static class WhatsNewCatalog
{
    private static readonly Dictionary<Version, WhatsNewEntry[]> Releases = new()
    {
        [new Version(0, 4, 0)] = new[]
        {
            Entry("0_4_0", 1, "alarm_clock"),     // notifications and snooze
            Entry("0_4_0", 2, "Flag24"),          // the important reminder that nags
            Entry("0_4_0", 3, "Desktop24"),       // tray and background running
            Entry("0_4_0", 4, "rocket"),          // start with Windows
            Entry("0_4_0", 5, "sparkler"),        // Remind UI 0.4
        },
        [new Version(0, 5, 0)] = new[]
        {
            Entry("0_5_0", 1, "sun_with_face"),   // the Today page
            Entry("0_5_0", 2, "megaphone"),       // the daily summary toast
            Entry("0_5_0", 3, "party_popper"),    // Remind UI 0.5
        },
    };

    private static WhatsNewEntry Entry(string ver, int n, string visual) =>
        new($"WhatsNew_{ver}_{n}_Title", $"WhatsNew_{ver}_{n}_Text", visual);

    /// <summary>"0.4.0" or "0.4.0-beta" or "0.4" as a Version (major.minor.patch); null when unreadable.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text.Trim(), @"^v?(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        try
        {
            return new Version(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
        }
        catch (OverflowException) { return null; }
    }

    /// <summary>The highlights for a version, or an empty list when that release has none.</summary>
    public static IReadOnlyList<WhatsNewEntry> For(string? version) =>
        ParseVersion(version) is { } v && Releases.TryGetValue(v, out var list) ? list : Array.Empty<WhatsNewEntry>();

    public static bool HasEntries(string? version) => For(version).Count > 0;

    /// <summary>Every version that has highlights.</summary>
    public static IEnumerable<Version> Versions => Releases.Keys;

    /// <summary>The newest version that has highlights (what Settings > About and the debug switch show).</summary>
    public static string Latest => Releases.Keys.Max()!.ToString(3);

    /// <summary>Picture or symbol for a visual name; null when it is neither (a typo must not crash the page).</summary>
    public static WhatsNewVisual? Resolve(string visual)
    {
        if (PictureCatalog.FromValue(PictureCatalog.BuiltinPrefix + visual) is { } picture) return new WhatsNewVisual(picture.Key, null);
        return Enum.TryParse<SymbolRegular>(visual, out var symbol) && Enum.IsDefined(symbol) ? new WhatsNewVisual(null, symbol) : null;
    }
}
