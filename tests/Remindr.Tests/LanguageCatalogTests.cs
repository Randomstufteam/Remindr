using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Remindr.Data.Repositories;
using Remindr.Services;

namespace Remindr.Tests;

/// <summary>The 176-language catalog: codes, cultures, direction, resolution, and what the pickers offer before every batch has landed.</summary>
public class LanguageCatalogTests
{
    /// <summary>The 144 languages added in Phase 4 part 3, as in PLAN.md.</summary>
    private static readonly string[] Added144 =
        ("af sq az-Latn eu bs-Latn br ca hr et fo gl ha is ig ga lv lt lb ms mt mi nn oc om rm gd sk sl so sw tk uz-Latn cy fy wo xh yo zu tn nso st rw rn sn lg ln ak ee ff-Latn ki mg to haw qu jv su ceb ku ast dsb hsb se kl eo kw gv kab nds fur sr-Latn ia gsw co bm ts ss nd kea luo dje sg mfe quc arn nyn ksh "
         + "be bg kk ky mk mn sr-Cyrl tg tt ba sah ce os hy ka "
         + "fa ur ps sd-Arab ug ckb pa-Arab ks-Arab dv yi am ti zgh "
         + "mr ne sa kok mai brx doi as mni-Beng pa gu or ta te kn ml si sat-Olck "
         + "bo dz my km lo gn la ve sc pap nr cv").Split(' ');

    private static readonly string[] Original32 =
    {
        "en", "es", "fr", "de", "it", "pt-BR", "pt-PT", "nl", "pl", "sv", "nb", "da", "fi", "cs", "hu", "ro", "el", "tr", "ru", "uk",
        "ar", "he", "hi", "bn", "id", "vi", "th", "ja", "ko", "zh-Hans", "zh-Hant", "fil"
    };

    private static readonly string[] Rtl12 = { "ar", "he", "fa", "ur", "ps", "sd-Arab", "ug", "ckb", "pa-Arab", "ks-Arab", "dv", "yi" };

    private static List<LanguageInfo> Real => LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode).ToList();

    [Fact]
    public void Catalog_HasExactly176Languages_Unique_AndTheOriginal32First()
    {
        Assert.Equal(144, Added144.Length);
        Assert.Equal(144, Added144.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(176, Real.Count);
        Assert.Equal(Original32.Concat(Added144).OrderBy(x => x, StringComparer.Ordinal), Real.Select(l => l.Code).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(Original32, Real.Take(32).Select(l => l.Code).ToArray());
        Assert.Equal(176, Real.Select(l => l.Code.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(176, Real.Select(l => l.NativeName).Distinct().Count());
        Assert.Equal(176, Real.Select(l => l.EnglishName).Distinct().Count());
        Assert.All(Real, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.NativeName), l.Code);
            Assert.False(string.IsNullOrWhiteSpace(l.EnglishName), l.Code);
        });
    }

    [Fact]
    public void EveryCode_IsAPredefinedCultureOnThisWindows()
    {
        var failed = new List<string>();
        foreach (var l in Real)
        {
            try
            {
                var c = CultureInfo.GetCultureInfo(l.Code, predefinedOnly: true);
                if (!string.Equals(c.Name, l.Code, StringComparison.OrdinalIgnoreCase)) failed.Add($"{l.Code} (became {c.Name})");
            }
            catch (CultureNotFoundException) { failed.Add(l.Code); }
        }
        Assert.True(failed.Count == 0, "Not predefined cultures here (swap each for another language Windows predefines): " + string.Join(", ", failed));
    }

    [Fact]
    public void IsRtl_MatchesTheCultureTextDirection_AndTheTwelveRtlLanguages()
    {
        Assert.Equal(Rtl12.OrderBy(x => x, StringComparer.Ordinal), Real.Where(l => l.IsRtl).Select(l => l.Code).OrderBy(x => x, StringComparer.Ordinal));
        // "ku" is Kurmanji (Latin script, left to right) here, but Windows' own "ku" culture reports right to left (it is the Arabic-script
        // Sorani default), so it is the one known exception; Sorani is listed separately as ckb.
        var wrong = Real.Where(l => l.Code != "ku" && CultureInfo.GetCultureInfo(l.Code, predefinedOnly: true).TextInfo.IsRightToLeft != l.IsRtl).Select(l => l.Code).ToList();
        Assert.True(wrong.Count == 0, "IsRtl differs from TextInfo.IsRightToLeft for: " + string.Join(", ", wrong));
    }

    [Fact]
    public void Reviewed_IsOnlyTheOriginal32_SoTheOtherHundredFortyFourAreTaggedMachineTranslated()
    {
        Assert.All(Real, l => Assert.Equal(Original32.Contains(l.Code), l.Reviewed));
        Assert.Equal(144, Real.Count(l => l.IsMachineTranslated));
        Assert.False(LocalizationService.Find("en")!.IsMachineTranslated);
    }

    [Fact]
    public void ForceGregorian_PersianShowsGregorianMonths_AndThaiTheGregorianYear()
    {
        var fa = LocalizationService.ForceGregorian(CultureInfo.GetCultureInfo("fa"));
        Assert.IsType<GregorianCalendar>(fa.DateTimeFormat.Calendar);
        var date = new DateTime(2026, 10, 25);
        // The Persian calendar would call this month (Mehr) and year 1405.
        Assert.Contains("2026", date.ToString("yyyy", fa));
        Assert.Equal(DateTimeFormatInfo.GetInstance(fa).GetMonthName(10), date.ToString("MMMM", fa));

        var th = LocalizationService.ForceGregorian(CultureInfo.GetCultureInfo("th-TH"));
        Assert.Equal("2026", date.ToString("yyyy", th));
        Assert.Equal("en", LocalizationService.ForceGregorian(CultureInfo.GetCultureInfo("en")).Name);
    }

    [Fact]
    public void ForceGregorian_EveryCatalogCulture_EndsUpGregorian()
    {
        var notGregorian = new List<string>();
        foreach (var l in Real)
        {
            var c = LocalizationService.ForceGregorian(CultureInfo.GetCultureInfo(l.Code, predefinedOnly: true));
            Assert.Equal(l.Code, c.Name, ignoreCase: true);
            if (c.DateTimeFormat.Calendar is not GregorianCalendar) notGregorian.Add(l.Code + ":" + c.DateTimeFormat.Calendar.GetType().Name);
        }
        Assert.True(notGregorian.Count == 0, "Display calendar is not Gregorian: " + string.Join(", ", notGregorian));
    }

    [Fact]
    public void ServiceCurrent_ForPersian_IsGregorian()
    {
        using var db = new TempDb();
        new Remindr.Data.DbInitializer(db.Db).Initialize();
        var svc = new LocalizationService(new SettingsRepo(db.Db), applyToProcess: false,
            windowsUi: CultureInfo.GetCultureInfo("en-US"), regional: CultureInfo.GetCultureInfo("en-US"));
        svc.Initialize();
        svc.SetLanguage("fa");
        Assert.IsType<GregorianCalendar>(svc.Current.DateTimeFormat.Calendar);
        Assert.True(svc.IsRtl);
    }

    [Theory]
    [InlineData("sr-Latn-RS", "sr-Latn")]
    [InlineData("sr-Cyrl-RS", "sr-Cyrl")]
    [InlineData("sr", "sr-Cyrl")]
    [InlineData("pa-Guru-IN", "pa")]
    [InlineData("pa-IN", "pa")]
    [InlineData("pa-Arab-PK", "pa-Arab")]
    [InlineData("pa-PK", "pa-Arab")]
    [InlineData("uz-Latn-UZ", "uz-Latn")]
    [InlineData("uz", "uz-Latn")]
    [InlineData("fa-AF", "fa")]
    [InlineData("fa-IR", "fa")]
    [InlineData("az-Latn-AZ", "az-Latn")]
    [InlineData("bs-Latn-BA", "bs-Latn")]
    [InlineData("mn-Cyrl-MN", "mn")]
    [InlineData("ff-Latn-SN", "ff-Latn")]
    [InlineData("sd-Arab-PK", "sd-Arab")]
    [InlineData("ks-Arab-IN", "ks-Arab")]
    [InlineData("mni-Beng-IN", "mni-Beng")]
    [InlineData("sat-Olck-IN", "sat-Olck")]
    [InlineData("ku-Arab-IQ", "ckb")]
    [InlineData("ckb-IQ", "ckb")]
    [InlineData("sw-KE", "sw")]
    [InlineData("nn-NO", "nn")]
    [InlineData("nb-NO", "nb")]
    [InlineData("no", "nb")]
    [InlineData("ca-ES", "ca")]
    [InlineData("zu-ZA", "zu")]
    [InlineData("quc-Latn-GT", "quc")]
    [InlineData("ar-SA", "ar")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("ji", "yi")]
    public void ResolveSupported_WalksUpToTheCatalogLanguage(string windows, string expected) =>
        Assert.Equal(expected, LocalizationService.ResolveSupported(windows));

    [Theory]
    [InlineData("uz-Cyrl-UZ")]
    [InlineData("tlh")]
    [InlineData("xx-XX")]
    public void ResolveSupported_ANameThatIsNotInTheCatalog_IsNull(string windows) =>
        Assert.Null(LocalizationService.ResolveSupported(windows));

    [Fact]
    public void Available_IsEnglishPlusEveryLanguageWithAResxFile_SoNoEnglishOnlyLanguageIsListed()
    {
        var files = Directory.EnumerateFiles(Path.Combine(SourceTree.AppDir(), "Resources"), "Strings.*.resx")
            .Select(f => Path.GetFileNameWithoutExtension(f)["Strings.".Length..]).ToList();
        var expected = new[] { "en" }.Concat(files).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var offered = LocalizationService.Available.Where(l => l.Code != LocalizationService.PseudoCode).Select(l => l.Code)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(expected, offered);
        // Catalog entries without text are still in Supported (the catalog is complete) but never offered.
        Assert.True(offered.Count <= 176);
        foreach (var l in Real.Where(l => !expected.Contains(l.Code, StringComparer.OrdinalIgnoreCase)))
            Assert.False(LocalizationService.IsTranslated(l.Code), l.Code);
    }

    [Fact]
    public void ResolveDefault_ALanguageWithoutTextYet_IsNotStartedIn()
    {
        // sw (Swahili) is in the catalog, but until its batch lands the app must not start in English under a Swahili name.
        if (!LocalizationService.IsTranslated("sw"))
        {
            Assert.Equal("en", LocalizationService.ResolveDefault(null, CultureInfo.GetCultureInfo("sw-KE")));
            Assert.Equal("en", LocalizationService.ResolveDefault("sw", CultureInfo.GetCultureInfo("en-US")));
        }
        // Nynorsk uses Bokmål while it has no text of its own.
        Assert.Equal(LocalizationService.IsTranslated("nn") ? "nn" : "nb", LocalizationService.ResolveDefault(null, CultureInfo.GetCultureInfo("nn-NO")));
    }

    [Fact]
    public void FontChains_CoverEveryScriptGroupWithShippedFontsFirst()
    {
        Assert.StartsWith("Ebrima", FontPolicy.FallbacksFor("am"));
        Assert.StartsWith("Ebrima", FontPolicy.FallbacksFor("zgh"));
        Assert.StartsWith("MV Boli", FontPolicy.FallbacksFor("dv"));
        Assert.StartsWith("Microsoft Himalaya", FontPolicy.FallbacksFor("bo"));
        Assert.StartsWith("Microsoft Himalaya", FontPolicy.FallbacksFor("dz"));
        Assert.StartsWith("Myanmar Text", FontPolicy.FallbacksFor("my"));
        
        Assert.StartsWith("Nirmala UI", FontPolicy.FallbacksFor("sat-Olck"));
        Assert.StartsWith("Nirmala UI", FontPolicy.FallbacksFor("si"));
        // Khmer and Lao: Leelawadee UI ships with Windows; the Features-on-Demand fonts come last.
        Assert.StartsWith("Leelawadee UI, Khmer UI", FontPolicy.FallbacksFor("km"));
        Assert.StartsWith("Leelawadee UI, Lao UI", FontPolicy.FallbacksFor("lo"));
        Assert.StartsWith("Segoe UI", FontPolicy.FallbacksFor("ur"));
    }

    [Fact]
    public void EveryCatalogLanguage_HasAChainWithSegoeUiAndTheBaseFontFirst()
    {
        foreach (var l in Real)
        {
            var chain = FontPolicy.Chain("Segoe UI Variable Text", l.Code);
            Assert.StartsWith("Segoe UI Variable Text, ", chain);
            Assert.Contains("Segoe UI", chain);
            Assert.Contains("Nirmala UI", chain);
        }
    }

    [Fact]
    public void CldrSource_IsPinnedAndHasTheOfficialHeader()
    {
        var text = File.ReadAllText(CldrPath());
        Assert.Contains("Unicode, Inc.", text);
        Assert.Contains("<pluralRules locales=\"ru uk\">", text);
    }

    internal static string CldrPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "tools", "cldr", "plurals.xml");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("tools\\cldr\\plurals.xml not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>PluralRules against every integer sample in the pinned CLDR plurals.xml.</summary>
public class PluralRulesCldrTests
{
    private static IEnumerable<(string Locales, string Category, long[] Samples)> CldrRules()
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(LanguageCatalogTests.CldrPath(), settings);
        var doc = new XmlDocument();
        doc.Load(reader);
        foreach (XmlElement group in doc.SelectNodes("//plurals[@type='cardinal']/pluralRules")!)
            foreach (XmlElement rule in group.SelectNodes("pluralRule")!)
            {
                var text = rule.InnerText;
                var at = text.IndexOf("@integer", StringComparison.Ordinal);
                var samples = new List<long>();
                if (at >= 0)
                {
                    var part = text[(at + "@integer".Length)..];
                    var dec = part.IndexOf("@decimal", StringComparison.Ordinal);
                    if (dec >= 0) part = part[..dec];
                    foreach (var raw in part.Split(','))
                    {
                        var token = raw.Trim();
                        // "…" ends the list; "1c6" style compact samples are not whole numbers we count.
                        if (token.Length == 0 || token.Contains('…') || token.Contains('c') || token.Contains('e')) continue;
                        var range = token.Split('~');
                        var a = long.Parse(range[0], CultureInfo.InvariantCulture);
                        var b = range.Length > 1 ? long.Parse(range[1], CultureInfo.InvariantCulture) : a;
                        for (var n = a; n <= b; n++) samples.Add(n);
                    }
                }
                yield return (group.GetAttribute("locales"), char.ToUpperInvariant(rule.GetAttribute("count")[0]) + rule.GetAttribute("count")[1..], samples.ToArray());
            }
    }

    [Fact]
    public void EveryIntegerSampleInCldr_GetsItsCategory_ForEveryLocale()
    {
        var wrong = new List<string>();
        var checkedSamples = 0;
        foreach (var (locales, category, samples) in CldrRules())
            foreach (var locale in locales.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(l => l != "root"))
                foreach (var n in samples)
                {
                    checkedSamples++;
                    var got = PluralRules.Category(locale.Replace('_', '-'), n);
                    if (got != category) wrong.Add($"{locale} {n}: CLDR {category}, got {got}");
                }
        Assert.True(checkedSamples > 5000, "samples read: " + checkedSamples);
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(30)));
    }

    [Theory]
    [InlineData("sl", 2, "Two")]
    [InlineData("sl", 3, "Few")]
    [InlineData("sl", 5, "Other")]
    [InlineData("sl", 101, "One")]
    [InlineData("cy", 0, "Zero")]
    [InlineData("cy", 1, "One")]
    [InlineData("cy", 2, "Two")]
    [InlineData("cy", 3, "Few")]
    [InlineData("cy", 6, "Many")]
    [InlineData("cy", 4, "Other")]
    [InlineData("ga", 7, "Many")]
    [InlineData("lv", 10, "Zero")]
    [InlineData("lt", 12, "Other")]
    [InlineData("sr-Latn", 22, "Few")]
    [InlineData("sr-Cyrl", 11, "Other")]
    [InlineData("be", 25, "Many")]
    [InlineData("mt", 11, "Many")]
    [InlineData("gd", 12, "Two")]
    [InlineData("pt-PT", 0, "Other")]
    [InlineData("pt-PT", 1, "One")]
    [InlineData("pt-BR", 0, "One")]
    [InlineData("fil", 5, "One")]
    [InlineData("fil", 4, "Other")]
    [InlineData("sk", 4, "Few")]
    [InlineData("rw", 3, "Other")]
    [InlineData("zzz", 1, "Other")]
    public void Spot_Checks(string language, long n, string expected) =>
        Assert.Equal(expected, PluralRules.Category(language, n));

    [Fact]
    public void EveryCatalogLanguage_HasAPluralRuleResult_AndOnlyKnownCategories()
    {
        var known = new HashSet<string> { "Zero", "One", "Two", "Few", "Many", "Other" };
        foreach (var l in LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode))
        {
            var categories = PluralRules.CategoriesFor(l.Code);
            Assert.NotEmpty(categories);
            Assert.All(categories, c => Assert.Contains(c, known));
        }
        // The languages of 0.3 keep what they had.
        Assert.Equal(new[] { "One", "Other" }, PluralRules.CategoriesFor("en").OrderBy(x => x));
        Assert.Equal(new[] { "Few", "Many", "One" }, PluralRules.CategoriesFor("ru").OrderBy(x => x));
        Assert.Equal(new[] { "Few", "Many", "One" }, PluralRules.CategoriesFor("pl").OrderBy(x => x));
        Assert.Equal(new[] { "Few", "One", "Other" }, PluralRules.CategoriesFor("cs").OrderBy(x => x));
        Assert.Equal(new[] { "Few", "Many", "One", "Other", "Two", "Zero" }, PluralRules.CategoriesFor("ar").OrderBy(x => x));
    }
}

/// <summary>The picker's search and ordering over the whole catalog (so it behaves when all 176 are listed).</summary>
public class LanguageSearchTests
{
    private static IReadOnlyList<LanguageInfo> Catalog => LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode).ToList();

    private static string[] Codes(string query) => LanguageSearch.Filter(query, Catalog).Select(l => l.Code).ToArray();

    [Fact]
    public void Port_FindsBothPortugueseAndNothingElse() =>
        Assert.Equal(new[] { "pt-BR", "pt-PT" }, Codes("port").OrderBy(c => c).ToArray());

    [Fact]
    public void Suomi_FindsFinnish() => Assert.Equal(new[] { "fi" }, Codes("suomi"));

    [Fact]
    public void Ethiopic_Amharic_IsFoundByBothSpellingsOfTheFirstLetter()
    {
        Assert.Contains("am", Codes("አማ"));
        Assert.Contains("am", Codes("ኣማ"));
    }

    [Theory]
    [InlineData("espanol", "es")]
    [InlineData("ESPAÑOL", "es")]
    [InlineData("francais", "fr")]
    [InlineData("turkce", "tr")]
    [InlineData("فارسی", "fa")]
    [InlineData("اردو", "ur")]
    [InlineData("german", "de")]
    [InlineData("hrvatski", "hr")]
    [InlineData("kiswahili", "sw")]
    [InlineData("ro", "ro")]
    public void SearchFindsByNativeNameEnglishNameOrCode_IgnoringCaseAndAccents(string query, string expected) =>
        Assert.Contains(expected, Codes(query));

    [Fact]
    public void ExactCodeOrNameStartComesBeforeAWordInsideAnotherName()
    {
        var found = Codes("fr");
        Assert.Equal("fr", found[0]);
        Assert.True(found.Length > 1);
    }

    [Fact]
    public void Unknown_FindsNothing_AndBlank_FindsEverything()
    {
        Assert.Empty(Codes("qqqqzz"));
        Assert.Equal(176, Codes("").Length);
        Assert.Equal(176, Codes("   ").Length);
    }

    [Fact]
    public void Arrange_PutsThePinnedLanguageFirst_ThenEnglishNamesInOrder()
    {
        var list = LanguageSearch.Arrange(Catalog, "ja");
        Assert.Equal("ja", list[0].Code);
        Assert.Equal(176, list.Count);
        var rest = list.Skip(1).Select(l => l.EnglishName).ToList();
        Assert.Equal(rest.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), rest);
        // An unknown pin changes nothing.
        Assert.Equal(176, LanguageSearch.Arrange(Catalog, "zz").Count);
    }
}

/// <summary>The same check as the Debug <c>--font-check</c> page: no native name needs a glyph that none of its fallback fonts has.</summary>
public class FontCoverageTests
{
    [Fact]
    public void EveryLanguageName_CanBeDrawnInItsFallbackChain()
    {
        var problems = new List<string>();
        foreach (var l in LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode))
        {
            var missing = FontCoverage.Missing("Segoe UI Variable Text", l.Code, l.NativeName);
            if (missing.Count > 0) problems.Add($"{l.Code} {l.NativeName}: {string.Join(" ", missing)}");
        }
        Assert.True(problems.Count == 0, "Characters with no glyph in the fallback chain (they would show as boxes):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Coverage_ActuallyDetectsAMissingGlyph()
    {
        // Private-use characters are in no font.
        Assert.NotEmpty(FontCoverage.Missing("Segoe UI", "en", ""));
        Assert.Empty(FontCoverage.Missing("Segoe UI", "en", "Remindr"));
    }
}
