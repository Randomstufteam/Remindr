using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Remindr.Services;

namespace Remindr.Tests;

/// <summary>Finds files of the app project above the test output folder.</summary>
internal static class SourceTree
{
    public static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "src", "Remindr");
            if (Directory.Exists(Path.Combine(p, "Resources"))) return p;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src\\Remindr not found above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(AppDir(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    public static Dictionary<string, string> ReadResx(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
}

public class ResxParityTests
{
    private static string ResourcesDir => Path.Combine(SourceTree.AppDir(), "Resources");

    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    private static string[] Placeholders(string value) =>
        Placeholder.Matches(value).Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [Fact]
    public void NeutralFile_HasKeysInAreaItemForm_AndNoEmptyValues()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        Assert.True(neutral.Count > 150, "expected the full English string table, got " + neutral.Count);
        Assert.All(neutral, kv =>
        {
            Assert.Matches(@"^[A-Z][A-Za-z0-9]*(_[A-Za-z0-9]+)+$", kv.Key);
            Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{kv.Key} is empty");
        });
    }

    [Fact]
    public void NeutralFile_FormatPatternsAreValidDatePatterns()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        var date = new DateTime(2026, 10, 24);
        foreach (var kv in neutral.Where(k => k.Key.StartsWith("Fmt_", StringComparison.Ordinal)))
            Assert.NotEqual(kv.Value, date.ToString(kv.Value, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void NeutralFile_PluralFamiliesHaveAnOtherForm()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        var bases = neutral.Keys.Where(k => Regex.IsMatch(k, "_(Zero|One|Two|Few|Many)$")).Select(k => k[..k.LastIndexOf('_')]).Distinct();
        Assert.All(bases, b => Assert.Contains(b + "_Other", neutral.Keys));
    }

    /// <summary>English text an extra plural key (e.g. X_Few) is checked against: X_Other, or null if it isn't a plural variant.</summary>
    static string? EnglishFor(IReadOnlyDictionary<string, string> neutral, string key)
    {
        foreach (var suffix in new[] { "_Two", "_Few", "_Many", "_Zero" })
            if (key.EndsWith(suffix, StringComparison.Ordinal)
                && neutral.TryGetValue(key[..^suffix.Length] + "_Other", out var english))
                return english;
        return null;
    }

    /// <summary>Runs over whatever Strings.&lt;culture&gt;.resx files exist.</summary>
    [Fact]
    public void EveryCultureFile_HasExactlyTheNeutralKeys_MatchingPlaceholders_AndNoEmptyValues()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        foreach (var path in Directory.EnumerateFiles(ResourcesDir, "Strings.*.resx"))
        {
            var file = Path.GetFileName(path);
            var other = SourceTree.ReadResx(path);

            Assert.True(!neutral.Keys.Except(other.Keys).Any(), $"{file} lacks: " + string.Join(", ", neutral.Keys.Except(other.Keys)));
            // Languages may add plural categories English lacks (key_Two/_Few/_Many/_Zero) next to an existing key_Other.
            var unknown = other.Keys.Where(k => !neutral.ContainsKey(k) && EnglishFor(neutral, k) is null).ToList();
            Assert.True(unknown.Count == 0, $"{file} has unknown keys: " + string.Join(", ", unknown));

            foreach (var (key, value) in other)
            {
                var english = neutral.TryGetValue(key, out var v) ? v : EnglishFor(neutral, key)!;
                Assert.False(string.IsNullOrWhiteSpace(value), $"{file}: {key} is empty");
                Assert.True(Placeholders(english).SequenceEqual(Placeholders(value)),
                    $"{file}: {key} placeholders differ. English: {english} / Translated: {value}");
            }
        }
    }

    [Fact]
    public void PlaceholderComparison_CatchesMismatches()
    {
        Assert.True(Placeholders("{0} open, {1} completed").SequenceEqual(Placeholders("{1} abgeschlossen, {0} offen")));
        Assert.False(Placeholders("Signed in as {0}").SequenceEqual(Placeholders("Angemeldet")));
        Assert.False(Placeholders("{0}, {1}").SequenceEqual(Placeholders("{0}, {2}")));
    }

    [Fact]
    public void CultureFileNames_AreSupportedLanguages()
    {
        // Strings.<culture>.resx: the culture must be one of the 32 languages (the pseudo-locale has no file).
        foreach (var f in Directory.EnumerateFiles(ResourcesDir, "Strings.*.resx"))
        {
            var culture = Path.GetFileNameWithoutExtension(f)["Strings.".Length..];
            Assert.NotNull(LocalizationService.Find(culture));
            Assert.NotEqual(LocalizationService.PseudoCode, culture);
        }
    }
}

public class ResourceKeyUsageTests
{
    private static readonly Regex XamlKey = new(@"\{l:Loc\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);
    private static readonly Regex CodeKey = new(@"Loc\.(?:Get|Format|Plural)\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    [Fact]
    public void EveryKeyUsedInXaml_ExistsInTheNeutralFile()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(SourceTree.AppDir(), "Resources", "Strings.resx"));
        var missing = new List<string>();
        foreach (var f in SourceTree.Files("*.xaml"))
            foreach (Match m in XamlKey.Matches(File.ReadAllText(f)))
                if (!neutral.ContainsKey(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
        Assert.Empty(missing);
    }

    // Any string literal shaped like one of our key areas must be a real key, or the start of a family built in code
    // (plural bases such as Calendar_DayCaption, "Settings_Theme_" + theme, greeting + "Named").
    private static readonly Regex KeyLiteral = new(
        @"""((?:Common|Nav|Today|Reminder|Group|Time|Card|AllReminders|Calendar|Category|Profile|Pin|Pictures|PictureCat|Avatar|Picker|Settings|Error|Fmt)_[A-Za-z0-9_]*)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryKeyUsedInCode_ExistsInTheNeutralFile()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(SourceTree.AppDir(), "Resources", "Strings.resx"));
        var missing = new List<string>();
        foreach (var f in SourceTree.Files("*.cs"))
        {
            var text = File.ReadAllText(f);
            foreach (Match m in KeyLiteral.Matches(text))
            {
                var key = m.Groups[1].Value;
                if (!neutral.Keys.Any(k => k == key || k.StartsWith(key, StringComparison.Ordinal)))
                    missing.Add($"{Path.GetFileName(f)}: {key}");
            }
            foreach (Match m in CodeKey.Matches(text))
            {
                var key = m.Groups[1].Value;
                if (!neutral.Keys.Any(k => k.StartsWith(key, StringComparison.Ordinal)))
                    missing.Add($"{Path.GetFileName(f)}: {key}");
            }
        }
        Assert.Empty(missing);
    }
    [Fact]
    public void EveryDynamicKeyFamily_Exists()
    {
        // Keys built from an enum or a suffix in code.
        var neutral = SourceTree.ReadResx(Path.Combine(SourceTree.AppDir(), "Resources", "Strings.resx"));
        foreach (var c in Enum.GetValues<AvatarCategory>()) Assert.Contains("PictureCat_" + c, neutral.Keys);
        foreach (var c in Enum.GetValues<ReminderCategory>()) Assert.Contains("PictureCat_" + c, neutral.Keys);
        foreach (var t in Enum.GetValues<Remindr.Models.AppTheme>()) Assert.Contains("Settings_Theme_" + t, neutral.Keys);
        foreach (var part in new[] { "Morning", "Afternoon", "Evening" })
        {
            Assert.Contains("Today_Greeting_" + part, neutral.Keys);
            Assert.Contains("Today_Greeting_" + part + "Named", neutral.Keys);
        }
    }

    [Fact]
    public void TranslationUrl_PrefillsTheLanguage_AndNothingPersonal()
    {
        var url = FeedbackLinks.TranslationUrl("de");
        Assert.Contains("template=translation.yml", url);
        Assert.Contains("language=German%20%28de%29", url);
        Assert.DoesNotContain("\\Users\\", url);
        Assert.True(File.Exists(Path.Combine(SourceTree.AppDir(), "..", "..", ".github", "ISSUE_TEMPLATE", "translation.yml")));
    }
}

/// <summary>
/// Hard-coded user-facing text in XAML would never be translated. This scan fails on a literal Text / Content / Header / ToolTip /
/// PlaceholderText / AutomationProperties.Name value that is not a binding or {l:Loc ...}, apart from symbols, numbers and the
/// brand name (see <see cref="Allowed"/>).
/// </summary>
public class XamlLiteralScanTests
{
    private static readonly Regex Attribute = new(
        @"(?<![\w.])(Text|Content|Header|ToolTip|PlaceholderText|AutomationProperties\.Name)\s*=\s*""([^""]*)""",
        RegexOptions.Compiled);

    // Values that are not words: the app name, punctuation glyphs used as icons, numbers.
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "Remindr", "!", "+", "…", "·", "–", "-", "?", "×", "✓" };

    private static bool IsAllowed(string value)
    {
        var v = System.Net.WebUtility.HtmlDecode(value).Trim();
        if (v.Length == 0 || Allowed.Contains(v)) return true;
        if (v.StartsWith("{", StringComparison.Ordinal)) return true;            // {Binding ...}, {l:Loc ...}, {StaticResource ...}, {x:Null}
        if (v.All(c => !char.IsLetter(c))) return true;                          // digits, glyphs, punctuation
        return false;
    }

    [Fact]
    public void XamlFiles_HaveNoHardCodedUserFacingText()
    {
        var offenders = new List<string>();
        foreach (var f in SourceTree.Files("*.xaml"))
        {
            var text = File.ReadAllText(f);
            foreach (Match m in Attribute.Matches(text))
                if (!IsAllowed(m.Groups[2].Value))
                {
                    var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(f)}:{line}  {m.Groups[1].Value}=\"{m.Groups[2].Value}\"");
                }
        }
        Assert.True(offenders.Count == 0, "Hard-coded text in XAML (use {l:Loc Key}):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Scan_ActuallyCatchesLiterals()
    {
        // Guards the scan itself: these must be flagged, bindings and symbols must not.
        Assert.False(IsAllowed("Save"));
        Assert.False(IsAllowed("Open log folder"));
        Assert.True(IsAllowed("{l:Loc Common_Save}"));
        Assert.True(IsAllowed("{Binding Title}"));
        Assert.True(IsAllowed("Remindr"));
        Assert.True(IsAllowed("!"));
        Assert.True(IsAllowed("&#x2026;"));
        Assert.True(IsAllowed("42"));
        Assert.Matches(Attribute, "<Button Content=\"Save\" />");
        Assert.Matches(Attribute, "<ui:TextBox PlaceholderText=\"x y\" />");
        Assert.DoesNotMatch(Attribute, "<Button DataContext=\"Save\" />");
    }
}
