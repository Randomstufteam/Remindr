using System.Globalization;
using Remindr.Data.Repositories;
using Remindr.Services;

namespace Remindr.Tests;

public class LanguageListTests
{
    private static readonly string[] Planned =
    {
        "en", "es", "fr", "de", "it", "pt-BR", "pt-PT", "nl", "pl", "sv", "nb", "da", "fi", "cs", "hu", "ro", "el", "tr", "ru", "uk",
        "ar", "he", "hi", "bn", "id", "vi", "th", "ja", "ko", "zh-Hans", "zh-Hant", "fil"
    };

    [Fact]
    public void Supported_StartsWithThe32PlannedLanguages_WithNativeNames()
    {
        var real = LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode).ToList();
        Assert.Equal(Planned, real.Take(32).Select(l => l.Code).ToArray());
        Assert.Equal(176, real.Count);
        Assert.All(real, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.NativeName), l.Code);
            Assert.False(string.IsNullOrWhiteSpace(l.EnglishName), l.Code);
        });
        Assert.Equal(real.Count, real.Select(l => l.NativeName).Distinct().Count());
        Assert.Equal(new[] { "ar", "he" }, real.Take(32).Where(l => l.IsRtl).Select(l => l.Code).ToArray());
    }

    [Fact]
    public void Supported_CodesAreValidCultureNames()
    {
        foreach (var l in LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode))
            Assert.Equal(l.Code, CultureInfo.GetCultureInfo(l.Code).Name, ignoreCase: true);
    }

    [Fact]
    public void PseudoLocale_IsOfferedInDebugBuildsOnly()
    {
        var offered = LocalizationService.Find(LocalizationService.PseudoCode) is not null;
#if DEBUG
        Assert.True(offered);
#else
        Assert.False(offered);
#endif
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("es-MX", "es")]
    [InlineData("es-419", "es")]
    [InlineData("en-GB", "en")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh-Hans-CN", "zh-Hans")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-MO", "zh-Hant")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-AO", "pt-BR")]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("nb-NO", "nb")]
    [InlineData("no", "nb")]
    [InlineData("fil-PH", "fil")]
    [InlineData("tl", "fil")]
    [InlineData("he-IL", "he")]
    [InlineData("ar-SA", "ar")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko-KR", "ko")]
    [InlineData("fr-CA", "fr")]
    public void ResolveSupported_MapsWindowsCulturesToSupportedLanguages(string windows, string expected) =>
        Assert.Equal(expected, LocalizationService.ResolveSupported(windows));

    [Theory]
    [InlineData("xx-XX")]
    [InlineData("tlh")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    public void ResolveSupported_UnknownIsNull(string? windows) =>
        Assert.Null(LocalizationService.ResolveSupported(windows));

    [Fact]
    public void ResolveDefault_SavedChoiceWinsOverWindows()
    {
        Assert.Equal("fr", LocalizationService.ResolveDefault("fr", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("ja", LocalizationService.ResolveDefault("ja", null));
    }

    [Fact]
    public void ResolveDefault_UsesWindowsLanguageWhenNothingSaved()
    {
        Assert.Equal("de", LocalizationService.ResolveDefault(null, CultureInfo.GetCultureInfo("de-AT")));
        Assert.Equal("es", LocalizationService.ResolveDefault(null, CultureInfo.GetCultureInfo("es-MX")));
        Assert.Equal("zh-Hant", LocalizationService.ResolveDefault(null, CultureInfo.GetCultureInfo("zh-TW")));
    }

    [Fact]
    public void ResolveDefault_UnsupportedFallsBackToEnglish()
    {
        Assert.Equal("en", LocalizationService.ResolveDefault(null, CultureInfo.GetCultureInfo("xx-XX")));
        Assert.Equal("en", LocalizationService.ResolveDefault(null, null));
        // A saved value that is not a supported language is ignored (e.g. a removed or Debug-only one).
        Assert.Equal("de", LocalizationService.ResolveDefault("klingon", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("en", LocalizationService.ResolveDefault("klingon", CultureInfo.GetCultureInfo("xx-XX")));
    }
}

/// <summary>The service itself, with applyToProcess off so nothing process-wide (thread cultures, Loc) is disturbed.</summary>
public class LocalizationServiceTests
{
    private static (LocalizationService Svc, SettingsRepo Repo, TempDb Db) Make(string windowsUi, string regional = "en-US", bool fresh = true,
        TempDb? existing = null)
    {
        var db = existing ?? new TempDb();
        if (fresh) new Remindr.Data.DbInitializer(db.Db).Initialize();
        var repo = new SettingsRepo(db.Db);
        var svc = new LocalizationService(repo, applyToProcess: false,
            windowsUi: CultureInfo.GetCultureInfo(windowsUi), regional: CultureInfo.GetCultureInfo(regional));
        return (svc, repo, db);
    }

    [Fact]
    public void Initialize_NothingSaved_UsesWindowsLanguage_AndDoesNotWrite()
    {
        var (svc, repo, db) = Make("de-AT");
        using (db)
        {
            svc.Initialize();
            Assert.Equal("de", svc.Code);
            Assert.Equal("de", svc.Current.TwoLetterISOLanguageName);
            Assert.Null(repo.Get(LocalizationService.SettingKey));
        }
    }

    [Fact]
    public void Initialize_UnsupportedWindowsLanguage_StartsInEnglish()
    {
        var (svc, _, db) = Make("xx-XX");
        using (db)
        {
            svc.Initialize();
            Assert.Equal("en", svc.Code);
        }
    }

    [Fact]
    public void Initialize_SavedValueWins()
    {
        var (svc, repo, db) = Make("de-DE");
        using (db)
        {
            repo.Set(LocalizationService.SettingKey, "ja");
            svc.Initialize();
            Assert.Equal("ja", svc.Code);
        }
    }

    [Fact]
    public void SetLanguage_Persists_AndANewServiceStartsWithIt()
    {
        var (svc, repo, db) = Make("en-US");
        using (db)
        {
            svc.Initialize();
            svc.SetLanguage("pt-BR");
            Assert.Equal("pt-BR", svc.Code);
            Assert.Equal("pt-BR", repo.Get("language"));

            var (again, _, _) = Make("de-DE", fresh: false, existing: db);
            again.Initialize();
            Assert.Equal("pt-BR", again.Code);
        }
    }

    [Fact]
    public void SetLanguage_RaisesCultureChangedOnlyWhenTheLanguageChanges()
    {
        var (svc, _, db) = Make("en-US");
        using (db)
        {
            svc.Initialize();
            var raised = 0;
            svc.CultureChanged += (_, _) => raised++;

            svc.SetLanguage("fr");
            Assert.Equal(1, raised);
            svc.SetLanguage("fr");
            Assert.Equal(1, raised);
            svc.SetLanguage("en");
            Assert.Equal(2, raised);
        }
    }

    [Fact]
    public void SetLanguage_UnsupportedCodeIsIgnored()
    {
        var (svc, repo, db) = Make("en-US");
        using (db)
        {
            svc.Initialize();
            svc.SetLanguage("tlh");
            Assert.Equal("en", svc.Code);
            Assert.Null(repo.Get("language"));
        }
    }

    [Fact]
    public void Current_UsesTheWindowsRegionOnlyWhenItSpeaksTheChosenLanguage()
    {
        var (svc, _, db) = Make("en-GB", regional: "en-GB");
        using (db)
        {
            svc.Initialize();
            Assert.Equal("en-GB", svc.Current.Name);                       // English UI keeps UK dates and Monday first
            Assert.Equal(DayOfWeek.Monday, CalendarMath.FirstDayOfWeek(svc.Current));

            svc.SetLanguage("de");
            Assert.Equal("de", svc.Current.Name);                          // German UI is not shown with UK formats
            Assert.Equal(DayOfWeek.Monday, CalendarMath.FirstDayOfWeek(svc.Current));
        }
    }

    [Fact]
    public void Current_FollowsTheChosenLanguageForFirstDayOfWeek()
    {
        var (svc, _, db) = Make("en-US", regional: "en-US");
        using (db)
        {
            svc.Initialize();
            Assert.Equal(DayOfWeek.Sunday, CalendarMath.FirstDayOfWeek(svc.Current));
            svc.SetLanguage("fr");
            Assert.Equal(DayOfWeek.Monday, CalendarMath.FirstDayOfWeek(svc.Current));
        }
    }

    [Fact]
    public void IsRtl_FollowsTheLanguage()
    {
        var (svc, _, db) = Make("en-US");
        using (db)
        {
            svc.Initialize();
            Assert.False(svc.IsRtl);
            svc.SetLanguage("ar");
            Assert.True(svc.IsRtl);
            svc.SetLanguage("he");
            Assert.True(svc.IsRtl);
            svc.SetLanguage("ja");
            Assert.False(svc.IsRtl);
        }
    }
}

public class LocTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Get_ReadsTheNeutralEnglishFile()
    {
        Assert.Equal("Cancel", Loc.Get("Common_Cancel", En));
        Assert.Equal("All day", Loc.Get("Time_AllDay", En));
        // A culture with no resx of its own (Afar isn't shipped) falls back to English.
        Assert.Equal("Cancel", Loc.Get("Common_Cancel", CultureInfo.GetCultureInfo("aa-ET")));
    }

    [Fact]
    public void Get_MissingKey_IsVisibleOnScreen()
    {
        Assert.Equal("!No_Such_Key!", Loc.Get("No_Such_Key", En));
        Assert.False(Loc.Has("No_Such_Key"));
        Assert.True(Loc.Has("Common_Cancel"));
    }

    [Fact]
    public void Get_Pseudo_AccentsPadsAndKeepsPlaceholders()
    {
        var text = Loc.Get("Settings_SignedIn", En, pseudo: true);
        Assert.StartsWith("[", text);
        Assert.EndsWith("]", text);
        Assert.Contains("{0}", text);
        Assert.DoesNotContain("Signed", text);
        Assert.True(text.Length > Loc.Get("Settings_SignedIn", En).Length * 1.3);
        // Date patterns are not text: they must stay patterns.
        Assert.Equal("MMMM yyyy", Loc.Get("Fmt_MonthYear", En, pseudo: true));
    }

    [Fact]
    public void Pseudo_Transform_IsReversibleByEye_AndPadsAboutAThird()
    {
        var p = Pseudo.Transform("Save changes");
        Assert.DoesNotContain("Save", p);
        Assert.Equal("Save changes".Length + 2 + "Save changes".Length * 35 / 100, p.Length);
        Assert.Equal("", Pseudo.Transform(""));
    }

    [Theory]
    [InlineData("en", 1, "One")]
    [InlineData("en", 0, "Other")]
    [InlineData("en", 2, "Other")]
    [InlineData("fr", 0, "One")]
    [InlineData("fr", 2, "Other")]
    [InlineData("ru", 1, "One")]
    [InlineData("ru", 21, "One")]
    [InlineData("ru", 3, "Few")]
    [InlineData("ru", 11, "Many")]
    [InlineData("ru", 25, "Many")]
    [InlineData("pl", 2, "Few")]
    [InlineData("pl", 12, "Many")]
    [InlineData("cs", 3, "Few")]
    [InlineData("cs", 5, "Other")]
    [InlineData("ar", 0, "Zero")]
    [InlineData("ar", 2, "Two")]
    [InlineData("ar", 7, "Few")]
    [InlineData("ar", 11, "Many")]
    [InlineData("ar", 100, "Other")]
    [InlineData("ja", 1, "Other")]
    [InlineData("zh-Hans", 5, "Other")]
    [InlineData("pt-BR", 0, "One")]
    [InlineData("pt-PT", 0, "Other")]
    [InlineData("he", 2, "Two")]
    public void PluralRules_Category(string language, long n, string expected) =>
        Assert.Equal(expected, PluralRules.Category(language, n));
}

public class FontPolicyTests
{
    [Fact]
    public void LatinFontStaysFirst_AndEachHanLanguageGetsItsOwnFontBeforeTheOthers()
    {
        Assert.StartsWith("Segoe UI Variable Text, ", FontPolicy.Chain("Segoe UI Variable Text", "de"));
        Assert.StartsWith("Yu Gothic UI", FontPolicy.FallbacksFor("ja"));
        Assert.StartsWith("Malgun Gothic", FontPolicy.FallbacksFor("ko"));
        Assert.StartsWith("Microsoft YaHei UI", FontPolicy.FallbacksFor("zh-Hans"));
        Assert.StartsWith("Microsoft JhengHei UI", FontPolicy.FallbacksFor("zh-Hant"));
    }

    [Fact]
    public void EveryLanguage_CoversThaiDevanagariBengaliArabicAndHebrew()
    {
        foreach (var lang in LocalizationService.Supported)
        {
            var chain = FontPolicy.FallbacksFor(lang.Code);
            Assert.Contains("Leelawadee UI", chain);   // Thai
            Assert.Contains("Nirmala UI", chain);      // Devanagari, Bengali
            Assert.Contains("Segoe UI", chain);        // Arabic, Hebrew
        }
    }
}
