using System.IO;
using Remindr.Data.Repositories;
using Remindr.Services;
using Remindr.ViewModels;

namespace Remindr.Tests;

/// <summary>Remind UI 0.4 slice 10: the bundled What's New content and the once-per-version rule.</summary>
[Collection("AnimGate")] // WhatsNewService.CurrentVersion is process-wide
public class WhatsNewTests
{
    [Theory]
    [InlineData("0.4.0", null, true, true, true)]      // upgrade from 0.3.x: no stored version, profiles exist
    [InlineData("0.4.0", null, false, true, false)]    // first run: no profile yet
    [InlineData("0.4.0", "0.4.0", true, true, false)]  // already seen
    [InlineData("0.4.1", "0.4.0", true, false, false)] // newer version without entries
    [InlineData("0.3.1", "0.4.0", true, true, false)]  // downgrade
    [InlineData("0.4.0", "0.3.1", true, true, true)]
    [InlineData("0.4.0", "garbage", true, true, true)]
    [InlineData("nonsense", "0.3.1", true, true, false)]
    [InlineData("0.4.0", null, true, false, false)]
    public void ShouldShow_Table(string current, string? lastSeen, bool profiles, bool entries, bool expected) =>
        Assert.Equal(expected, WhatsNewService.ShouldShow(current, lastSeen, profiles, entries));

    [Fact]
    public void Catalog_Has04Entries_EveryKeyExists_EveryVisualResolves()
    {
        var neutral = SourceTree.ReadResx(Path.Combine(SourceTree.AppDir(), "Resources", "Strings.resx"));
        var entries = WhatsNewCatalog.For("0.4.0");
        Assert.InRange(entries.Count, 4, 6);
        foreach (var e in entries)
        {
            Assert.True(neutral.ContainsKey(e.TitleKey), e.TitleKey);
            Assert.True(neutral.ContainsKey(e.TextKey), e.TextKey);
            Assert.NotNull(WhatsNewCatalog.Resolve(e.Visual));
        }
        Assert.Equal("0.4.0", WhatsNewCatalog.Latest);
        Assert.False(WhatsNewCatalog.HasEntries("0.3.1"));
        Assert.True(WhatsNewCatalog.HasEntries("0.4.0-beta"));
        Assert.Null(WhatsNewCatalog.Resolve("not_a_thing"));
        Assert.NotNull(WhatsNewCatalog.Resolve("Flag24")!.Symbol);
        Assert.Equal("builtin:alarm_clock", WhatsNewCatalog.Resolve("alarm_clock")!.PictureValue);
    }

    [Fact]
    public void VersionedKeys_AreTranslatedInEveryShippedLanguage_AndFallBackToEnglishInOthers()
    {
        var dir = Path.Combine(SourceTree.AppDir(), "Resources");
        var de = SourceTree.ReadResx(Path.Combine(dir, "Strings.de.resx"));
        foreach (var k in new[] { "WhatsNew_Title", "WhatsNew_GotIt", "WhatsNew_Open", "WhatsNew_0_4_0_1_Title", "WhatsNew_0_4_0_5_Text" }) Assert.Contains(k, de.Keys);
        Assert.Equal("What's new in Remindr 0.4", Loc.Format("WhatsNew_Title", "0.4"));
        // German has its own card text now (the string freeze); a language with no file yet falls back to English.
        Assert.NotEqual(Loc.Get("WhatsNew_0_4_0_1_Title", System.Globalization.CultureInfo.InvariantCulture),
            Loc.Get("WhatsNew_0_4_0_1_Title", System.Globalization.CultureInfo.GetCultureInfo("de")));
        if (!Remindr.Services.LocalizationService.IsTranslated("sw"))
        Assert.Equal(Loc.Get("WhatsNew_0_4_0_1_Title", System.Globalization.CultureInfo.InvariantCulture),
            Loc.Get("WhatsNew_0_4_0_1_Title", System.Globalization.CultureInfo.GetCultureInfo("sw")));
    }

    private static (TempDb T, SettingsRepo S, ProfileRepo P, WhatsNewService W) Make()
    {
        var t = new TempDb();
        new Remindr.Data.DbInitializer(t.Db).Initialize();
        var s = new SettingsRepo(t.Db);
        var p = new ProfileRepo(t.Db, Path.Combine(t.Dir, "avatars"));
        return (t, s, p, new WhatsNewService(s, p));
    }

    [Fact]
    public void Service_UpgradeFlow_ShowsOnce_ThenNever()
    {
        var old = WhatsNewService.CurrentVersion;
        WhatsNewService.CurrentVersion = "0.4.0";
        var (t, s, p, w) = Make();
        try
        {
            Assert.False(w.ShouldShowNow());                 // no profile: fresh install
            p.Create("Sam", "#3B82F6", "S");
            Assert.True(w.ShouldShowNow());                  // existing 0.3.x data, nothing stored
            w.MarkSeen();
            Assert.Equal("0.4.0", s.Get(WhatsNewService.LastSeenKey));
            Assert.False(w.ShouldShowNow());

            // Downgrade: running 0.3.1 with 0.4.0 stored neither shows nor lowers the stored version.
            WhatsNewService.CurrentVersion = "0.3.1";
            Assert.False(w.ShouldShowNow());
            w.MarkSeen();
            Assert.Equal("0.4.0", s.Get(WhatsNewService.LastSeenKey));
        }
        finally { WhatsNewService.CurrentVersion = old; t.Dispose(); }
    }

    [Fact]
    public void SetupComplete_WritesTheCurrentVersion_SoNewUsersNeverSeeIt()
    {
        var old = WhatsNewService.CurrentVersion;
        WhatsNewService.CurrentVersion = "0.4.0";
        var (t, s, p, w) = Make();
        try
        {
            var vm = new SetupViewModel(p, "en", Remindr.Models.AppTheme.Dark) { Name = "Sam" };
            vm.Complete();
            Assert.Equal("0.4.0", s.Get(WhatsNewService.LastSeenKey));
            Assert.False(w.ShouldShowNow());
        }
        finally { WhatsNewService.CurrentVersion = old; t.Dispose(); }
    }

    [Fact]
    public void LaunchArgs_DebugSwitches_AreParsed()
    {
        var a = LaunchArgs.Parse(new[] { "--whatsnew", "--pretend-version=0.4.0" });
        Assert.True(a.WhatsNew);
        Assert.Equal("0.4.0", a.PretendVersion);
        Assert.Null(LaunchArgs.Parse(new[] { "--pretend-version=abc" }).PretendVersion);
        Assert.False(LaunchArgs.Parse(Array.Empty<string>()).WhatsNew);
    }
}

/// <summary>Remind UI 0.4 slice 11: the overlay's wiring and the B-lite release notes list.</summary>
public class WhatsNewOverlayTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SourceTree.AppDir() }.Concat(parts).ToArray()));

    [Fact]
    public void ReleaseNotes_KeepsHeadingsAndBullets_AsPlainText()
    {
        var body = "## What's new\r\n\r\n- **Faster** startup, see [the docs](https://example.com)\r\n* Fixed `crash` <b>on</b> save\r\n![shot](https://example.com/a.png)\r\n---\r\nPlain line\r\n1. Numbered";
        var lines = ReleaseNotes.Parse(body);
        Assert.Equal(new[]
        {
            new NoteLine(NoteLineKind.Heading, "What's new"),
            new NoteLine(NoteLineKind.Bullet, "Faster startup, see the docs"),
            new NoteLine(NoteLineKind.Bullet, "Fixed crash on save"),
            new NoteLine(NoteLineKind.Text, "Plain line"),
            new NoteLine(NoteLineKind.Bullet, "Numbered"),
        }, lines);
    }

    [Fact]
    public void ReleaseNotes_EmptyOrHostile_IsEmptyAndBounded()
    {
        Assert.Empty(ReleaseNotes.Parse(null));
        Assert.Empty(ReleaseNotes.Parse("  \n \n---\n![x](y)"));
        var many = ReleaseNotes.Parse(string.Join("\n", Enumerable.Range(0, 500).Select(i => "- item " + i)));
        Assert.Equal(ReleaseNotes.MaxLines, many.Count);
        Assert.All(ReleaseNotes.Parse(new string('x', 5000)), l => Assert.True(l.Text.Length <= ReleaseNotes.MaxLineLength + 1));
    }

    [Fact]
    public void ReleaseParser_ReadsTheBody()
    {
        var json = "{\"tag_name\":\"v0.4.0\",\"html_url\":\"https://github.com/Randomstufteam/Remindr/releases/tag/v0.4.0\",\"body\":\"- one\",\"assets\":[]}";
        var r = ReleaseParser.Parse(json, new UpdatePolicy());
        Assert.Equal("- one", r!.Body);
        Assert.Null(ReleaseParser.Parse(json.Replace(",\"body\":\"- one\"", ""), new UpdatePolicy())!.Body);
    }

    [Fact]
    public void DisplayVersion_DropsATrailingZeroPatch()
    {
        Assert.Equal("0.4", WhatsNewService.DisplayVersion("0.4.0"));
        Assert.Equal("0.4.1", WhatsNewService.DisplayVersion("0.4.1"));
    }

    [Fact]
    public void Overlay_IsWiredIntoTheShell_BelowDialogs_AndSettingsHasTheEntryPoint()
    {
        var main = Read("MainWindow.xaml");
        var overlay = main.IndexOf("x:Name=\"WhatsNewOverlay\"", StringComparison.Ordinal);
        Assert.True(overlay > main.IndexOf("x:Name=\"Setup\"", StringComparison.Ordinal));
        Assert.True(overlay < main.IndexOf("x:Name=\"DialogHost\"", StringComparison.Ordinal));
        Assert.Contains("ShowWhatsNewCommand", Read("Views", "Pages", "SettingsPage.xaml"));
        var view = Read("Views", "Welcome", "WhatsNewView.xaml");
        Assert.Contains("AutomationProperties.LiveSetting=\"Off\"", view);
        Assert.Contains("Elevation3", view);
        Assert.Contains("RadiusLarge", view);
        Assert.Contains("MaxWidth=\"720\"", view);
        Assert.Contains("MaxHeight=\"560\"", view);
    }
}
