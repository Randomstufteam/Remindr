using System.IO;
using System.Text.RegularExpressions;
using Remindr.Services;

namespace Remindr.Tests;

/// <summary>
/// The string freeze: no translation file may quietly keep English text, every plural family has a form for every category its language
/// produces, and the bundled What's New highlights are translated everywhere.
/// </summary>
public class TranslationFreezeTests
{
    private static string ResourcesDir => Path.Combine(SourceTree.AppDir(), "Resources");

    private static IEnumerable<(string Culture, string Path)> CultureFiles() =>
        Directory.EnumerateFiles(ResourcesDir, "Strings.*.resx").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (System.IO.Path.GetFileNameWithoutExtension(f)["Strings.".Length..], f));

    /// <summary>Keys whose text is a name, a symbol pattern or a date/time pattern, so it is right to be the same in every language.</summary>
    private static readonly string[] SameInEveryLanguage =
    {
        "Common_Ok",                          // "OK" is written the same nearly everywhere
        "Settings_Version",                   // "Remindr {0}"
        "Settings_RemindUiVersion",           // "Remind UI {0}": a name
        "WhatsNew_0_4_0_5_Title",             // "Remind UI 0.4": a name
        "WhatsNew_0_5_0_3_Title",             // "Remind UI 0.5": a name
        "Summary_Join",                       // "{0}, {1}": only the separator differs by script
        "Picker_PinName",                     // "PIN"
        "Calendar_WeekRangeCrossMonth",       // "{0} – {1}"
        "Calendar_WeekRangeCrossYear",
        "Calendar_WeekRangeSameMonth",
        "Calendar_DayAutomation",             // "{0}, {1}"
        "Card_DateAndTime",                   // "{0}, {1}"
    };

    /// <summary>
    /// Reviewed exceptions: a real word that is spelled like the English one in that language (agenda, personal, notifications), or a loanword
    /// the language keeps (Filipino). Add to this only after a native speaker or a dictionary confirms it. Format: "culture:key".
    /// </summary>
    private static readonly HashSet<string> ReviewedSameAsEnglish = new(StringComparer.Ordinal)
    {
        "cs:Calendar_Agenda", "de:Calendar_Agenda", "es:Calendar_Agenda", "fi:Calendar_Agenda", "fil:Calendar_Agenda", "fr:Calendar_Agenda",
        "id:Calendar_Agenda", "it:Calendar_Agenda", "nb:Calendar_Agenda", "nl:Calendar_Agenda", "pl:Calendar_Agenda", "pt-BR:Calendar_Agenda",
        "pt-PT:Calendar_Agenda", "sv:Calendar_Agenda",
        "fr:Calendar_DayAutomationImportant", "ro:Calendar_DayAutomationImportant",   // "{0}, {1}, important" (important is the same word)
        "nl:Calendar_Week",
        "es:Category_Personal", "fil:Category_Personal", "ro:Category_Personal",
        "nl:Category_School", "nl:PictureCat_School",
        "fr:Common_Date", "id:Common_Edit", "fr:Common_Important", "ro:Common_Important", "fr:Common_Notes",
        "ro:Nav_Calendar",
        "fr:PictureCat_Nature",
        "es:Profile_Color", "de:Profile_Name", "de:Profile_PinOptional",
        "de:Reminder_NotesPlaceholder",
        "fr:Settings_Notifications",
        "da:Settings_TestToast_Button", "nb:Settings_TestToast_Button",                // "Send test"
        "da:Settings_Theme_System", "de:Settings_Theme_System", "fil:Settings_Theme_System", "nb:Settings_Theme_System", "sv:Settings_Theme_System",
        "de:Settings_Updates", "nl:Settings_Updates", "nl:Update_Later",
        "fr:Toast_AttrImportant", "ro:Toast_AttrImportant",
        "fr:Toast_Snooze5",                                                             // "5 minutes"
        // Filipino keeps these English words (and the English date patterns, which are patterns, not text).
        "fil:Nav_Profile", "fil:Settings_Profile", "fil:Settings_PinLock", "fil:Settings_PinStatus",
        // Phase 4 batches 14-21: words that are spelled the same in these languages.
        "af:Calendar_Agenda", "bs-Latn:Calendar_Agenda", "ca:Calendar_Agenda", "cy:Calendar_Agenda", "eu:Calendar_Agenda",
        "nn:Calendar_Agenda", "sr-Latn:Calendar_Agenda",
        "af:Calendar_Week", "af:Update_Later",                                           // Afrikaans "Week", "Later"
        "ca:Common_Notes", "ca:Common_Important", "ca:Calendar_DayAutomationImportant", "ca:Toast_AttrImportant",
        "ca:Profile_Color", "ca:PictureCat_Animals", "ca:Category_Personal",
        "cy:Settings_Theme_System", "nn:Settings_Theme_System", "hsb:Settings_Theme_System",
        "fy:Settings_Updates",                                                           // same loanword as Dutch
        "nds:Calendar_Week", "nds:Pictures_All", "nds:PictureCat_School", "nds:Category_School", "nds:Update_Later",   // Low German spells these the same
    };

    private static bool IsAllowedSame(string culture, string key) =>
        SameInEveryLanguage.Contains(key)
        || key.StartsWith("Fmt_", StringComparison.Ordinal)                 // date patterns
        || ReviewedSameAsEnglish.Contains(culture + ":" + key);

    [Theory]
    [InlineData("Error_Unhandled")]
    [InlineData("Error_Startup")]
    public void CrashMessages_KeepTheBlankLineAndTheLogPath_InEveryLanguage(string key)
    {
        var broken = new List<string>();
        foreach (var (culture, path) in CultureFiles())
        {
            var value = SourceTree.ReadResx(path)[key].Replace("\r\n", "\n");
            if (!value.Contains("\n\n") || value.Contains("\\n") || !value.Contains(@"%AppData%\Remindr\error.log")) broken.Add(culture);
        }
        Assert.True(broken.Count == 0, key + " is broken in: " + string.Join(", ", broken));
    }

    [Fact]
    public void NoCultureFileKeepsEnglishText_ExceptTheReviewedAllowList()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        var leftovers = new List<string>();
        foreach (var (culture, path) in CultureFiles())
            foreach (var (key, value) in SourceTree.ReadResx(path))
            {
                var english = neutral.TryGetValue(key, out var v) ? v : EnglishPluralBase(neutral, key);
                if (english is null || !string.Equals(value, english, StringComparison.Ordinal)) continue;
                if (!IsAllowedSame(culture, key)) leftovers.Add($"{culture}:{key} = \"{value}\"");
            }
        Assert.True(leftovers.Count == 0,
            "English text left in translation files (translate it, or add a reviewed exception to ReviewedSameAsEnglish):\n" + string.Join("\n", leftovers.Take(60)));
    }

    [Fact]
    public void TheAllowList_OnlyNamesKeysAndCulturesThatExist_AndEachEntryIsStillNeeded()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        var cultures = CultureFiles().ToDictionary(c => c.Culture, c => SourceTree.ReadResx(c.Path));
        var stale = new List<string>();
        foreach (var entry in ReviewedSameAsEnglish)
        {
            var parts = entry.Split(':');
            if (!neutral.ContainsKey(parts[1])) { stale.Add(entry + " (no such key)"); continue; }
            if (!cultures.TryGetValue(parts[0], out var file)) { stale.Add(entry + " (no such language)"); continue; }
            if (!file.TryGetValue(parts[1], out var value) || value != neutral[parts[1]]) stale.Add(entry + " (no longer English)");
        }
        Assert.True(stale.Count == 0, "Stale allow-list entries:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void TheNoEnglishCheck_ActuallyFires()
    {
        // A value copied from English is flagged; names and reviewed cognates are not.
        Assert.False(IsAllowedSame("de", "Toast_Dismiss"));
        Assert.False(IsAllowedSame("es", "WhatsNew_Open"));
        Assert.True(IsAllowedSame("de", "Common_Ok"));
        Assert.True(IsAllowedSame("fil", "Fmt_MonthDay"));
        Assert.True(IsAllowedSame("fr", "Calendar_Agenda"));
        Assert.False(IsAllowedSame("ja", "Calendar_Agenda"));
    }

    private static string? EnglishPluralBase(IReadOnlyDictionary<string, string> neutral, string key)
    {
        foreach (var suffix in new[] { "_Two", "_Few", "_Many", "_Zero", "_ZeroForm" })
            if (key.EndsWith(suffix, StringComparison.Ordinal) && neutral.TryGetValue(key[..^suffix.Length] + "_Other", out var english))
                return english;
        return null;
    }

    [Fact]
    public void EveryCultureFile_HasAFormForEveryPluralCategoryItsLanguageProduces_InEveryFamily()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        var families = neutral.Keys.Where(k => k.EndsWith("_Other", StringComparison.Ordinal)).Select(k => k[..^"_Other".Length])
            .Where(b => neutral.ContainsKey(b + "_One")).ToList();
        Assert.Contains("Tray_TooltipDue", families);
        Assert.Contains("Calendar_DayCaption", families);

        var missing = new List<string>();
        foreach (var (culture, path) in CultureFiles())
        {
            var file = SourceTree.ReadResx(path);
            // "Zero" is the message for exactly 0 (or the separate _ZeroForm); it is never required.
            var categories = PluralRules.CategoriesFor(culture).Where(c => c != "Zero").ToList();
            foreach (var family in families)
                foreach (var category in categories)
                    if (!file.ContainsKey(family + "_" + category)) missing.Add($"{culture}: {family}_{category}");
        }
        Assert.True(missing.Count == 0, "Missing plural forms:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryCultureFile_HasTheWhatsNewHighlightsOfTheBundledReleases()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(ResourcesDir, "Strings.resx"));
        var whatsNew = neutral.Keys.Where(k => Regex.IsMatch(k, @"^WhatsNew_0_[45]_0_")).ToList();
        Assert.Equal(16, whatsNew.Count);
        foreach (var (culture, path) in CultureFiles())
        {
            var file = SourceTree.ReadResx(path);
            var missing = whatsNew.Where(k => !file.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{culture} lacks: " + string.Join(", ", missing));
        }
    }

    [Fact]
    public void EveryLanguageWithAFile_IsInTheCatalog_AndTheCatalogOffersItInThePicker()
    {
        foreach (var (culture, _) in CultureFiles())
        {
            Assert.NotNull(LocalizationService.Find(culture));
            Assert.Contains(LocalizationService.Available, l => l.Code == culture);
            Assert.True(LocalizationService.IsTranslated(culture), culture);
        }
    }
}
