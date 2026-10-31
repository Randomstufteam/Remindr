using System.Globalization;
using System.Windows.Automation;

namespace Remindr.Shots;

/// <summary>Opens the app (or installer) on fresh demo data in a temp folder, over a neutral backdrop.</summary>
internal sealed class Rig : IDisposable
{
    private readonly Options _o;
    private readonly Backdrop _bd = new();
    private readonly List<string> _dirs = new();
    public string Theme { get; }

    public Rig(Options o, string theme)
    {
        _o = o;
        Theme = theme;
        _bd.Start(theme == "Dark");
    }

    public AppWindow OpenApp(bool empty, string lang = "en", bool reduced = true, params string[] appArgs)
    {
        var dir = Demo.NewTempDir(empty ? "first-run" : "demo");
        _dirs.Add(dir);
        if (empty) Demo.SeedEmpty(dir, Theme, lang); else Demo.SeedProfiles(dir, Theme, lang);
        var app = AppWindow.Launch(_o.AppExe, dir, reduced, appArgs);
        Shooter.Place(app);
        app.Need(id: empty ? "WelcomeView" : "Picker", timeoutMs: 20000);
        return app;
    }

    public void Dispose()
    {
        _bd.Dispose();
        foreach (var d in _dirs) { try { Directory.Delete(d, true); } catch (Exception) { /* a locked temp file is harmless */ } }
    }
}

internal static class ShotScenes
{
    private static int _saved;

    private static void Save(Options o, string name, Frame f)
    {
        var p = Path.Combine(o.OutDir, name + ".png");
        Png.Save(f, p);
        _saved++;
        Console.WriteLine($"  {name}.png  {f.Width}x{f.Height}  {new FileInfo(p).Length / 1024} KB");
    }

    public static int Run(Options o)
    {
        Directory.CreateDirectory(o.OutDir);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var t = theme.ToLowerInvariant();
            using var rig = new Rig(o, theme);
            Console.WriteLine($"== {theme}");
            if (o.Wants("welcome") || o.Wants("setup")) FirstRun(o, rig, t);
            if (o.Wants("picker") || o.Wants("avatar")) PickerAndAvatar(o, rig, t);
            if (o.Wants("all-reminders") || o.Wants("month") || o.Wants("week") || o.Wants("agenda") || o.Wants("edit") || o.Wants("settings"))
                MainApp(o, rig, t);
        }
        using (var rig = new Rig(o, "Light"))
        {
            Console.WriteLine("== Languages (Light)");
            Languages(o, rig);
        }
        if (o.Wants("installer")) Installer(o);
        Console.WriteLine($"Saved {_saved} images to {o.OutDir}");
        return 0;
    }

    // ---- first run: greeting and setup (empty data folder) ----
    private static void FirstRun(Options o, Rig rig, string t)
    {
        using var app = rig.OpenApp(empty: true);
        if (o.Wants("welcome")) Save(o, "welcome-" + t, Shooter.GrabSteady(app));
        if (!o.Wants("setup")) return;
        AppWindow.Activate(app.Need(id: "WelcomeStart"));
        app.Need(id: "SetupLanguageSearch");
        if (o.Wants("setup-language")) Save(o, "setup-language-" + t, Shooter.GrabSteady(app));
        AppWindow.Activate(app.Need(id: "SetupNext"));
        app.Need(id: "SetupThemeList");
        if (o.Wants("setup-appearance")) Save(o, "setup-appearance-" + t, Shooter.GrabSteady(app));
        AppWindow.Activate(app.Need(id: "SetupNext"));
        var name = app.Need(id: "SetupName");
        AppWindow.SetText(name, "Maya");
        AppWindow.Activate(app.Need("#8B5CF6", within: app.Need(id: "SetupColors")));
        var picker = app.Need(id: "Picker");
        AppWindow.Activate(app.Need("Family and roles", type: ControlType.Button, within: picker));
        app.Settle(500);
        AppWindow.Activate(app.Need("Student", type: ControlType.Button, within: picker));
        app.Settle(600);
        if (o.Wants("setup-profile")) Save(o, "setup-profile-" + t, Shooter.GrabSteady(app));
    }

    // ---- profile picker and the avatar chooser ----
    private static void PickerAndAvatar(Options o, Rig rig, string t)
    {
        using var app = rig.OpenApp(empty: false);
        if (o.Wants("picker")) Save(o, "profile-picker-" + t, Shooter.GrabSteady(app));
        if (!o.Wants("avatar")) return;
        AppWindow.Activate(app.Need("Edit or delete", type: ControlType.Button));
        AppWindow.Activate(app.Need("Edit", type: ControlType.MenuItem));
        app.Need(id: "NameBox");
        var dialogPicker = app.Need("Family and roles", type: ControlType.Button);
        AppWindow.Activate(dialogPicker);
        app.Settle(600);
        Save(o, "avatar-chooser-" + t, Shooter.GrabSteady(app));
    }

    // ---- the main app on Maya's profile ----
    private static void MainApp(Options o, Rig rig, string t)
    {
        using var app = rig.OpenApp(empty: false);
        Flow.EnterProfile(app, "Maya");

        if (o.Wants("all-reminders"))
        {
            Flow.NavIndex(app, false, 2);
            Save(o, "all-reminders-" + t, Shooter.GrabSteady(app));
        }

        if (o.Wants("month") || o.Wants("week") || o.Wants("agenda"))
        {
            Flow.NavIndex(app, false, 1);
            var day = DateTime.Today.AddDays(2).ToString("dddd, MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
            AppWindow.Activate(app.Need(day, type: ControlType.Button, exact: false));
            app.Settle(700);
            if (o.Wants("month")) Save(o, "month-" + t, Shooter.GrabSteady(app));
            if (o.Wants("week"))
            {
                AppWindow.Activate(app.Need("Week view"));
                app.Settle(900);
                Save(o, "week-" + t, Shooter.GrabSteady(app));
            }
            if (o.Wants("agenda"))
            {
                AppWindow.Activate(app.Need("Agenda view"));
                app.Settle(900);
                Save(o, "agenda-" + t, Shooter.GrabSteady(app));
            }
        }

        if (o.Wants("settings"))
        {
            Flow.NavIndex(app, true, 1);
            var upd = app.Find(id: "UpdateCheckToggle");
            if (upd is not null) AppWindow.Toggle(upd, true);
            app.Settle(500);
            if (o.Wants("settings-language")) Save(o, "settings-language-" + t, Shooter.GrabSteady(app));
            Flow.ScrollPageToEnd(app);
            if (o.Wants("settings-about")) Save(o, "settings-about-" + t, Shooter.GrabSteady(app));
        }

        if (o.Wants("edit"))
        {
            Flow.NavIndex(app, false, 2);
            Flow.EditCard(app, "Dentist appointment");
            Flow.OpenPicturePopup(app);
            AppWindow.Activate(app.Need("Health", type: ControlType.Button));
            app.Settle(600);
            Save(o, "edit-dialog-" + t, Shooter.GrabSteady(app));
        }
    }

    // ---- other languages ----
    private static void Languages(Options o, Rig rig)
    {
        if (o.Wants("month-ja"))
        {
            using var app = rig.OpenApp(false, "ja");
            Flow.EnterProfile(app, "Maya");
            Flow.NavIndex(app, false, 1);
            app.Settle(500);
            Save(o, "month-ja-light", Shooter.GrabSteady(app));
        }
        if (o.Wants("all-reminders-ar"))
        {
            using var app = rig.OpenApp(false, "ar");
            Flow.EnterProfile(app, "Maya");
            Flow.NavIndex(app, false, 2);
            app.Settle(500);
            Save(o, "all-reminders-ar-light", Shooter.GrabSteady(app));
        }
        if (o.Wants("settings-de"))
        {
            using var app = rig.OpenApp(false, "de");
            Flow.EnterProfile(app, "Maya");
            Flow.NavIndex(app, true, 1);
            app.Settle(500);
            Save(o, "settings-de-light", Shooter.GrabSteady(app));
        }
    }

    // ---- installer pages (--demo writes nothing) ----
    private static void Installer(Options o)
    {
        using var bd = new Backdrop();
        bd.Start(false);
        var dir = Demo.NewTempDir("installer");
        using var app = AppWindow.Launch(o.SetupExe, dir, false, "--demo");
        app.Settle(1800);
        Frame Page(string name)
        {
            Native.SetWindowPos(app.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0003 | Native.SWP_NOACTIVATE);
            Thread.Sleep(500);
            var f = Shooter.Grab(app, false);
            // The window has a 28 px transparent margin around the glass card; keep just the card, with rounded corners.
            var card = f.Crop(28, 28, f.Width - 56, f.Height - 56);
            card.RoundCorners(20);
            Save(o, name, card);
            return card;
        }
        if (o.Wants("installer-welcome")) Page("installer-welcome");
        AppWindow.Activate(app.Need(id: "PrimaryButton"));
        app.Need(id: "DesktopBox");
        if (o.Wants("installer-options")) Page("installer-options");
        AppWindow.Activate(app.Need(id: "PrimaryButton"));
        // Installing: grab around the middle of the progress.
        for (var i = 0; i < 60; i++)
        {
            var pct = app.Find(id: "Percent")?.Current.Name ?? "";
            if (int.TryParse(pct.TrimEnd('%'), out var v) && v is >= 38 and <= 62) break;
            Thread.Sleep(40);
        }
        if (o.Wants("installer-installing")) Page("installer-installing");
        app.Need(id: "PrimaryButton", timeoutMs: 20000);
        app.Settle(1500);
        if (o.Wants("installer-done")) Page("installer-done");
    }
}
