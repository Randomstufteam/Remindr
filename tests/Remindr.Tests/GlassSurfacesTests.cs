using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Controls;

namespace Remindr.Tests;

/// <summary>Remind UI 0.4 slice 9: empty-state float, toast pictures and attribution, and the tray status.</summary>
[Collection("AnimGate")] // Anim.Lookup is process-wide state
public class GlassSurfacesTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SourceTree.AppDir() }.Concat(parts).ToArray()));

    private static Reminder Sample(Action<Reminder>? tweak = null)
    {
        var r = new Reminder { Id = 7, ProfileId = 3, Title = "Dentist", DueDate = new DateOnly(2026, 10, 27), DueTime = new TimeOnly(15, 0) };
        tweak?.Invoke(r);
        return r;
    }

    private static string Xml(Reminder r, bool overdue = false, string? profile = "Sam", Uri? logo = null) =>
        ToastContent.Build(r, new PlannedAlert(NotificationKind.AtTime, NotificationPlanner.OccurrenceKey(r), overdue), "Today 3:00 PM", profile, logo)
            .GetToastContent().GetContent();

    // ---- toast cache -----------------------------------------------------------------------------------

    [Fact]
    public void ToastCache_MakesAFileOnce_AndCleanRemovesOnlyStaleFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "remindr-toast-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ToastImageCache(dir);
            var renders = 0;
            byte[]? Png() { renders++; return new byte[] { 1, 2, 3 }; }

            var first = cache.GetOrCreate("builtin:books", Png);
            var second = cache.GetOrCreate("builtin:books", Png);
            Assert.NotNull(first);
            Assert.Equal(first, second);
            Assert.Equal(1, renders);                       // drawn once, then reused
            Assert.True(File.Exists(first));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp")); // no half-written leftovers

            Assert.Null(cache.GetOrCreate("builtin:nothing", () => null));   // nothing to draw: no file, no throw

            cache.Clean();
            Assert.True(File.Exists(first));                // a fresh picture survives: a toast may still point at it
            File.WriteAllBytes(first + ".tmp", new byte[] { 1 });
            cache.Clean(DateTime.UtcNow + ToastImageCache.MaxAge + TimeSpan.FromHours(1));
            Assert.Empty(Directory.GetFiles(dir));          // unused for a week (and leftovers) are removed
            cache.Clean();                                // an already-empty / missing folder is fine
            new ToastImageCache(Path.Combine(dir, "missing")).Clean();
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ToastCache_FileNamesAreSafe()
    {
        var name = ToastImageCache.FileName("file:avatars/3.png-123");
        Assert.Matches(@"^[A-Za-z0-9_-]+-96\.png$", name);
        Assert.DoesNotContain(":", name);
    }

    [Fact]
    public void PickPicture_ReminderFirst_ThenAvatar_ThenNone()
    {
        var withAvatar = new Profile { Id = 3, Name = "Sam", Avatar = "builtin:fox" };
        Assert.Equal("builtin:books", ToastImageCache.PickPicture(Sample(r => r.Icon = "builtin:books"), withAvatar));
        Assert.Equal("builtin:fox", ToastImageCache.PickPicture(Sample(), withAvatar));
        Assert.Equal("builtin:fox", ToastImageCache.PickPicture(Sample(r => r.Icon = "builtin:not_a_picture"), withAvatar)); // unknown key
        Assert.Null(ToastImageCache.PickPicture(Sample(), new Profile { Name = "Sam" }));
        Assert.Null(ToastImageCache.PickPicture(Sample(), null));
    }

    [Fact]
    public void KeyFor_UploadedAvatarChangesWithTheFile_BuiltinDoesNot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "remindr-av-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal("builtin:fox", ToastImageCache.KeyFor("builtin:fox", dir));
            var file = Path.Combine(dir, "3.png");
            File.WriteAllBytes(file, new byte[] { 1 });
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var before = ToastImageCache.KeyFor("file:avatars/3.png", dir);
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.NotEqual(before, ToastImageCache.KeyFor("file:avatars/3.png", dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LogoRenderer_DrawsA96PixelPng_ForABuiltinPicture_AndNothingForAnUnknownOne()
    {
        Sta.Run(() =>
        {
            var bytes = ToastLogoRenderer.Render("builtin:books");
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length is > 100 and < 150_000, "size " + bytes.Length);   // Windows may drop toast images over ~200 KB
            using var ms = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(ms);
            Assert.Equal(96, frame.PixelWidth);
            Assert.Equal(96, frame.PixelHeight);
            // The corner is outside the circle: fully transparent.
            var px = new byte[4];
            frame.CopyPixels(new Int32Rect(0, 0, 1, 1), px, 4, 0);
            Assert.Equal(0, px[3]);

            Assert.Null(ToastLogoRenderer.Render("builtin:not_a_picture"));
        });
    }

    // ---- toast content ---------------------------------------------------------------------------------

    [Fact]
    public void ImportantToast_UsesTheReminderScenario_AndAttributesTheProfile()
    {
        var xml = Xml(Sample(r => r.IsImportant = true));
        Assert.Contains("scenario=\"reminder\"", xml);
        Assert.Contains("Important \u00B7 Sam", xml);
        Assert.Contains("placement=\"attribution\"", xml);
    }

    [Fact]
    public void OverdueToast_SaysSoInTheAttribution_AndBothJoinInOrder()
    {
        var overdue = Xml(Sample(), overdue: true);
        Assert.Contains("Overdue \u00B7 Sam", overdue);
        Assert.DoesNotContain("scenario=\"reminder\"", overdue);      // only important toasts stay on screen
        Assert.Contains("Important \u00B7 Overdue \u00B7 Sam", Xml(Sample(r => r.IsImportant = true), overdue: true));
    }

    [Fact]
    public void PlainToast_HasNoAttribution_NoScenario_AndTheButtonsAreStillThere()
    {
        var xml = Xml(Sample());
        Assert.DoesNotContain("attribution", xml);
        Assert.DoesNotContain("scenario", xml);
        var doc = XDocument.Parse(xml);
        Assert.Equal(3, doc.Descendants("action").Count());
    }

    [Fact]
    public void ToastPicture_IsACircleCroppedAppLogoOverride()
    {
        var uri = new Uri(@"C:\data\toast-cache\builtin_books-96.png");
        var xml = Xml(Sample(), logo: uri);
        Assert.Contains("placement=\"appLogoOverride\"", xml);
        Assert.Contains("hint-crop=\"circle\"", xml);
        Assert.DoesNotContain("appLogoOverride", Xml(Sample()));
    }

    [Fact]
    public void Attribution_OmitsAMissingProfileName()
    {
        Assert.Equal("Important", ToastContent.Attribution(true, false, null));
        Assert.Equal("Important", ToastContent.Attribution(true, false, "  "));
        Assert.Null(ToastContent.Attribution(false, false, "Sam"));
    }

    // ---- tray status ----------------------------------------------------------------------------------

    [Fact]
    public void TrayStatus_CountsAcrossAllProfiles_AndIgnoresDoneAndSnoozedOnes()
    {
        using var e = new ReminderEnv(new DateTime(2026, 10, 24, 10, 30, 15));
        e.Repo.Create(ReminderEnv.R("Later today", "2026-10-24", "18:00"));
        e.Repo.Create(ReminderEnv.R("All day", "2026-10-24"));
        var done = e.Repo.Create(ReminderEnv.R("Done", "2026-10-24", "08:00"));
        e.Repo.SetCompleted(done.Id, true);
        Assert.Equal(new TrayStatus(2, 0), e.Repo.CountTrayStatus(e.Clock.Now));       // nothing overdue yet: no dot

        var past = e.Repo.Create(ReminderEnv.R("Missed", "2026-10-24", "09:00"));      // earlier today
        e.Repo.Create(ReminderEnv.R("Yesterday", "2026-10-23"));
        e.Session.SwitchTo(e.B);
        e.Repo.Create(ReminderEnv.R("Other profile", "2026-10-24", "09:30"));
        e.Session.SwitchTo(e.A);
        var status = e.Repo.CountTrayStatus(e.Clock.Now);
        Assert.Equal(new TrayStatus(4, 3), status);                                    // every profile counts
        Assert.True(status.HasAlert);

        e.Repo.SetSnoozedUntil(past.Id, new DateTime(2026, 10, 24, 12, 0, 0));
        Assert.Equal(2, e.Repo.CountTrayStatus(e.Clock.Now).Overdue);                  // snoozed into the future: not nagging
        e.Repo.SetCompleted(past.Id, true);
        Assert.Equal(2, e.Repo.CountTrayStatus(e.Clock.Now).Overdue);                  // completing removes it from the count
    }

    [Fact]
    public void TrayStatus_NothingOpen_HasNoAlertAndAPlainTooltip()
    {
        using var e = new ReminderEnv();
        var status = e.Repo.CountTrayStatus(e.Clock.Now);
        Assert.Equal(TrayStatus.None, status);
        Assert.False(status.HasAlert);
        Assert.Equal("Remindr", status.Tooltip);
    }

    [Fact]
    public void TrayTooltip_IsPluralAware()
    {
        Assert.Equal("Remindr \u00B7 1 due today", new TrayStatus(1, 0).Tooltip);
        Assert.Equal("Remindr \u00B7 2 due today", new TrayStatus(2, 5).Tooltip);
    }

    [Fact]
    public void TrayIcons_AlertVariantDiffersFromTheNormalOne_AndHasTheDotInTheCorner()
    {
        var path = Path.Combine(SourceTree.AppDir(), "Assets", "remindr.ico");
        using var plain = TrayIcons.Create(File.OpenRead(path), alert: false);
        using var alert = TrayIcons.Create(File.OpenRead(path), alert: true);
        Assert.NotNull(plain);
        Assert.NotNull(alert);
        using var a = plain!.ToBitmap();
        using var b = alert!.ToBitmap();
        var dot = TrayIcons.DotBounds(b.Width, b.Height);
        var c = b.GetPixel((int)(dot.X + dot.Width / 2), (int)(dot.Y + dot.Height / 2));
        Assert.Equal(TrayIcons.AlertColor.R, c.R);
        Assert.Equal(TrayIcons.AlertColor.G, c.G);
        Assert.Equal(TrayIcons.AlertColor.B, c.B);
        var p = a.GetPixel((int)(dot.X + dot.Width / 2), (int)(dot.Y + dot.Height / 2));
        Assert.False(p.R == c.R && p.G == c.G && p.B == c.B);   // the normal icon has no dot there
    }

    // ---- float and wiring ---------------------------------------------------------------------------

    private static ResourceDictionary Tokens(bool motion = true, bool visual = true)
    {
        using var s = File.OpenRead(Path.Combine(SourceTree.AppDir(), "Styles", "Animations.xaml"));
        var d = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(s);
        d["MotionEnabled"] = motion;
        d["VisualEffectsEnabled"] = visual;
        return d;
    }

    private static void WithTokens(ResourceDictionary d, Action a)
    {
        var old = Anim.Lookup;
        Anim.Lookup = key => d.Contains(key) ? d[key] : null;
        try { a(); } finally { Anim.Lookup = old; }
    }

    [Fact]
    public void Float_RunsOnlyWhenLoadedVisibleAndTheGateIsOn() => Sta.Run(() =>
    {
        var el = new Border();                         // not loaded: never floats
        WithTokens(Tokens(), () => Assert.False(Anim.ShouldFloat(el, null)));

        // Gate off / reduced motion: even a loaded, visible element does not float (DurFloat is zero under reduced motion).
        var reduced = new ResourceDictionary();
        MotionPolicy.ApplyReducedMotion(reduced, new[] { Tokens() });
        WithTokens(reduced, () => Assert.False(Anim.ShouldFloat(el, null)));
        WithTokens(Tokens(visual: false), () => Assert.False(Anim.ShouldFloat(el, null)));
    });

    [Fact]
    public void FloatToken_ExistsAndIsZeroedUnderReducedMotion()
    {
        Assert.Contains("x:Key=\"DurFloat\"", Read("Styles", "Animations.xaml"));
        Sta.Run(() =>
        {
            var reduced = new ResourceDictionary();
            MotionPolicy.ApplyReducedMotion(reduced, new[] { Tokens() });
            Assert.Equal(new Duration(TimeSpan.Zero), (Duration)reduced["DurFloat"]);
        });
    }

    [Fact]
    public void EmptyStates_UseAFloatingPicture_NotTheOldSymbolIcon()
    {
        foreach (var (file, key) in new[]
        {
            ("AllRemindersPage.xaml", "builtin:clipboard"), ("TodayPage.xaml", "builtin:sun_with_face"),
            ("CalendarPage.xaml", "builtin:tear_off_calendar"), ("CalendarPage.xaml", "builtin:calendar")
        })
        {
            var xaml = Read("Views", "Pages", file);
            Assert.Contains($"Picture=\"{key}\"", xaml);
            Assert.NotNull(PictureCatalog.FromValue(key));   // and the picture exists
        }
        Assert.DoesNotContain("FontSize=\"32\"", Read("Views", "Pages", "AllRemindersPage.xaml"));
        Assert.DoesNotContain("FontSize=\"32\"", Read("Views", "Pages", "TodayPage.xaml"));
    }

    [Fact]
    public void TrayMenu_UsesGlassTokens_AndIsMergedIntoTheApp()
    {
        var xaml = Read("Styles", "TrayMenu.xaml");
        Assert.Contains("RadiusSmall", xaml);
        Assert.Contains("GlassStrokeBrush", xaml);
        Assert.Contains("GlassPillHoverBrush", xaml);
        Assert.Contains("Styles/TrayMenu.xaml", Read("App.xaml"));
    }
}
