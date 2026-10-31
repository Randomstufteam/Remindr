using System.Windows.Automation;

namespace Remindr.Shots;

/// <summary>
/// <c>--l10n</c>: one run through the pages that stress scripts and reading direction (Month, Settings with the language picker open,
/// What's New, the edit dialog) at 1000x640, for one language per script group plus every right-to-left language. Temp data folder,
/// demo data, reduced motion. Output goes to docs/l10n-check/ (gitignored) unless <c>--out</c> is given.
/// </summary>
internal static class L10n
{
    // One language per script group (Latin with tall diacritics, Cyrillic, Greek, ... Tibetan, Myanmar, Khmer, Lao, Thai, the CJK three).
    public static readonly string[] ScriptGroups =
    {
        "vi", "ru", "el", "hy", "ka", "am", "zgh", "hi", "bn", "pa", "gu", "or", "ta", "te", "kn", "ml", "si", "sat-Olck",
        "bo", "my", "km", "lo", "th", "ja", "ko", "zh-Hans",
    };

    // Every right-to-left language in LanguageCatalog (12; the test pins this list to the catalog).
    public static readonly string[] Rtl = { "ar", "he", "fa", "ur", "ps", "sd-Arab", "ug", "ckb", "pa-Arab", "ks-Arab", "dv", "yi" };

    private const int W = 1000, H = 640;
    private static int _saved;

    public static int Run(Options o)
    {
        var langs = o.Langs.Length > 0 ? o.Langs : ScriptGroups.Concat(Rtl).Distinct().ToArray();
        var outDir = o.OutDirSet ? o.OutDir : Path.Combine(o.Docs, "l10n-check");
        Directory.CreateDirectory(outDir);
        var failed = new List<string>();
        using var rig = new Rig(o, "Light");
        foreach (var lang in langs)
        {
            Console.WriteLine("== " + lang);
            try { One(o, rig, lang, outDir); }
            catch (Exception ex) { Console.WriteLine($"  FAILED {lang}: {ex.Message}"); failed.Add(lang); }
        }
        Console.WriteLine($"Saved {_saved} images for {langs.Length - failed.Count}/{langs.Length} languages to {outDir}");
        if (failed.Count > 0) Console.WriteLine("Failed: " + string.Join(", ", failed));
        return failed.Count == 0 ? 0 : 1;
    }

    private static void Save(string dir, string name, AppWindow app)
    {
        var f = Shooter.GrabSteady(app, false, 5000);
        if (f.Width != W || f.Height != H) f = f.Scaled(W, H);
        Png.Save(f, Path.Combine(dir, name + ".png"));
        _saved++;
    }

    private static void Step(string what, Action a)
    {
        try { a(); }
        catch (Exception ex) { Console.WriteLine($"  {what}: skipped ({ex.GetType().Name}: {ex.Message.Split('\n')[0]}) {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
    }

    private static void One(Options o, Rig rig, string lang, string dir)
    {
        using var app = rig.OpenApp(empty: false, lang, reduced: true, "--pretend-version=0.4.0");
        Shooter.Place(app, W, H);
        Flow.EnterProfile(app, "Maya");
        var id = lang;

        // With --pretend-version=0.4.0 (Debug build) the page opens by itself after the profile; it blocks the nav until dismissed.
        Step("whatsnew", () =>
        {
            app.Need(id: "WhatsNewView", timeoutMs: 6000); app.Settle(1200);
            Save(dir, id + "-whatsnew", app);
            AppWindow.Activate(app.Need(id: "WhatsNewGotIt"));
            app.Settle(900);
        });

        Step("month", () => { Flow.NavIndex(app, false, 1); app.Settle(500); Save(dir, id + "-month", app); });

        Step("settings", () =>
        {
            Flow.NavIndex(app, true, 1);
            app.Settle(500);
            Save(dir, id + "-settings", app);
            var btn = app.Need(id: "SettingsLanguageButton");
            AppWindow.Activate(btn);
            app.Need(id: "SettingsLanguageButton"); app.Settle(900);
            Save(dir, id + "-picker", app);
            AppWindow.Activate(btn);   // toggles the popup shut (no keyboard input is ever sent)
            app.Settle(500);
        });

        Step("edit", () =>
        {
            Flow.NavIndex(app, false, 2);
            var title = app.Need(id: "TitleText");
            var card = TreeWalker.ControlViewWalker.GetParent(title);
            var edit = card.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            AppWindow.Activate(edit);
            app.Need(id: "TitleBox"); app.Settle(700);
            Save(dir, id + "-edit", app);
        });
    }
}
