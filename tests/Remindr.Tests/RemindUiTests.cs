using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Remindr.Services;
using Remindr.Views.Controls;

namespace Remindr.Tests;

/// <summary>Remind UI 0.4 foundation: version, the motion tokens, the single effects gate and its helpers.</summary>
[Collection("AnimGate")] // Anim.Lookup is process-wide state
public class RemindUiTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AnimationsXamlPath => Path.Combine(SourceTree.AppDir(), "Styles", "Animations.xaml");

    private static ResourceDictionary LoadAnimations()
    {
        using var s = File.OpenRead(AnimationsXamlPath);
        return (ResourceDictionary)XamlReader.Load(s);
    }

    private static ResourceDictionary Tokens(bool motion = true, bool visual = true)
    {
        var d = LoadAnimations();
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

    // ---- version and strings -----------------------------------------------------------------------------

    [Fact]
    public void Version_IsZeroPointFour() => Assert.Equal("0.4", RemindUI.Version);

    [Fact]
    public void RemindUiVersionString_ExistsEverywhere_AndNeverTranslatesTheName()
    {
        var dir = Path.Combine(SourceTree.AppDir(), "Resources");
        foreach (var f in Directory.EnumerateFiles(dir, "Strings*.resx"))
        {
            var all = SourceTree.ReadResx(f);
            Assert.True(all.TryGetValue("Settings_RemindUiVersion", out var v), Path.GetFileName(f));
            Assert.Contains("Remind UI", v);
            Assert.Contains("{0}", v);
        }
        Assert.Equal("Remind UI 0.4", Loc.Format("Settings_RemindUiVersion", RemindUI.Version));
    }

    // ---- tokens --------------------------------------------------------------------------------------

    [Fact]
    public void Animations_HasTheNewTokens()
    {
        var keys = XDocument.Load(AnimationsXamlPath).Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).ToHashSet();
        foreach (var k in new[] { "DurEnter", "DurExit", "StaggerRevealMs", "EasePop", "EaseInOut", "VisualEffectsEnabled", "MotionEnabled" })
            Assert.Contains(k, keys);
    }

    [Fact]
    public void Animations_EveryDurationAndStaggerToken_IsNamedSoReducedMotionFindsIt()
    {
        foreach (var e in XDocument.Load(AnimationsXamlPath).Root!.Elements())
        {
            var key = (string?)e.Attribute(X + "Key");
            if (key is null) continue;
            if (e.Name.LocalName == "Duration") Assert.True(MotionPolicy.IsDurationKey(key), key + " is a Duration but not named Dur*");
            if (key.Contains("Stagger", StringComparison.Ordinal)) Assert.True(MotionPolicy.IsStaggerKey(key) && e.Name.LocalName == "Double", key);
        }
    }

    [Fact]
    public void ReducedMotion_ZeroesEveryDurAndStaggerKeyInAnimationsXaml()
    {
        Sta.Run(() =>
        {
            var source = LoadAnimations();
            var expected = XDocument.Load(AnimationsXamlPath).Root!.Elements()
                .Select(e => (string?)e.Attribute(X + "Key"))
                .Where(k => k is not null && (MotionPolicy.IsDurationKey(k) || MotionPolicy.IsStaggerKey(k)))
                .Select(k => k!).ToList();
            Assert.True(expected.Count >= 10, "scan found only " + expected.Count);

            var target = new ResourceDictionary();
            MotionPolicy.ApplyReducedMotion(target, new[] { source });

            foreach (var key in expected)
            {
                Assert.True(target.Contains(key), key + " was not zeroed");
                if (source[key] is Duration) Assert.Equal(new Duration(TimeSpan.Zero), (Duration)target[key]);
                else Assert.Equal(0.0, (double)target[key]);
            }
            Assert.Equal(false, target["MotionEnabled"]);
        });
    }

    [Fact]
    public void ReducedMotion_FindsKeysInMergedDictionaries()
    {
        Sta.Run(() =>
        {
            var app = new ResourceDictionary();
            app.MergedDictionaries.Add(LoadAnimations());
            MotionPolicy.ApplyReducedMotion(app, app.MergedDictionaries);
            Assert.Equal(new Duration(TimeSpan.Zero), (Duration)app["DurEnter"]);
            Assert.Equal(0.0, (double)app["StaggerRevealMs"]);
        });
    }

    [Fact]
    public void ReducedMotion_LeavesWpfUiResourcesAlone()
    {
        // The real App merges WPF-UI's dictionaries too; the name scan must only ever touch Remindr's own tokens.
        Sta.Run(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.Create(new Uri("reliable://0")); // registers pack:// outside an Application
            _ = new System.Windows.Documents.FlowDocument();
            var app = new ResourceDictionary();
            app.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
            app.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            Assert.True(app.MergedDictionaries.Sum(m => m.Keys.Count + m.MergedDictionaries.Sum(x => x.Keys.Count)) > 50, "WPF-UI dictionaries did not load");
            var target = new ResourceDictionary();
            MotionPolicy.ApplyReducedMotion(target, app.MergedDictionaries);
            Assert.Equal(new[] { "MotionEnabled" }, target.Keys.OfType<string>().ToArray());
        });
    }

    // ---- --no-effects --------------------------------------------------------------------------------

    [Fact]
    public void NoEffectsArg_IsParsed()
    {
        Assert.True(LaunchArgs.Parse(new[] { "--no-effects" }).NoEffects);
        Assert.True(LaunchArgs.Parse(new[] { "--minimized", "--NO-EFFECTS" }).NoEffects);
        Assert.False(LaunchArgs.Parse(new[] { "--minimized" }).NoEffects);
        Assert.False(LaunchArgs.Parse(null).NoEffects);
    }

    [Fact]
    public void NoEffects_TurnsTheGateOff_InDebugOnly()
    {
        Sta.Run(() =>
        {
            var debug = Tokens();
            MotionPolicy.ApplyLaunchArgs(debug, LaunchArgs.Parse(new[] { "--no-effects" }), debugBuild: true);
            WithTokens(debug, () => { Assert.True(Anim.MotionEnabled); Assert.False(Anim.EffectsEnabled); });

            var release = Tokens();
            MotionPolicy.ApplyLaunchArgs(release, LaunchArgs.Parse(new[] { "--no-effects" }), debugBuild: false);
            WithTokens(release, () => Assert.True(Anim.EffectsEnabled));

            var plain = Tokens();
            MotionPolicy.ApplyLaunchArgs(plain, LaunchArgs.Parse(Array.Empty<string>()), debugBuild: true);
            WithTokens(plain, () => Assert.True(Anim.EffectsEnabled));
        });
    }

    // ---- the gate ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void EffectsEnabled_IsMotionAndVisualEffects(bool motion, bool visual, bool expected)
    {
        Sta.Run(() => WithTokens(Tokens(motion, visual), () => Assert.Equal(expected, Anim.EffectsEnabled)));
    }

    [Fact]
    public void EffectsEnabled_IsReadEachTime_NotCached()
    {
        Sta.Run(() =>
        {
            var d = Tokens();
            WithTokens(d, () =>
            {
                Assert.True(Anim.EffectsEnabled);
                d["VisualEffectsEnabled"] = false; // what Phase 6.9 will do at runtime
                Assert.False(Anim.EffectsEnabled);
                d["VisualEffectsEnabled"] = true;
                Assert.True(Anim.EffectsEnabled);
            });
        });
    }

    // ---- helpers are no-ops when the gate is off ---------------------------------------------------------

    private static void GateOffCases(Action<ResourceDictionary> body) => Sta.Run(() =>
    {
        body(Tokens(visual: false));   // --no-effects
        var reduced = new ResourceDictionary();
        MotionPolicy.ApplyReducedMotion(reduced, new[] { LoadAnimations() });
        body(reduced);                  // Windows animations off: zero durations, MotionEnabled false
    });

    [Fact]
    public void Pop_GateOff_LeavesTheElementUntouched() => GateOffCases(d => WithTokens(d, () =>
    {
        var el = new Border();
        Anim.Pop(el);
        Assert.True(ReferenceEquals(el.RenderTransform, Transform.Identity));
        Assert.Equal(1.0, el.Opacity);
    }));

    [Fact]
    public void SlideSwap_GateOff_SetsTheEndStateAtOnce() => GateOffCases(d => WithTokens(d, () =>
    {
        var oldEl = new Border { Visibility = Visibility.Visible };
        var newEl = new Border { Visibility = Visibility.Collapsed, Opacity = 0.3 };
        Anim.SlideSwap(oldEl, newEl, 1);
        Assert.Equal(Visibility.Collapsed, oldEl.Visibility);
        Assert.Equal(Visibility.Visible, newEl.Visibility);
        Assert.Equal(1.0, newEl.Opacity);
        Assert.True(ReferenceEquals(newEl.RenderTransform, Transform.Identity));
        Assert.True(ReferenceEquals(oldEl.RenderTransform, Transform.Identity));
    }));

    [Fact]
    public void PressScale_GateOff_DoesNothing() => GateOffCases(d => WithTokens(d, () =>
    {
        var el = new Border();
        Anim.SetPressScale(el, true);
        Anim.SetPressed(el, true);
        Assert.True(ReferenceEquals(el.RenderTransform, Transform.Identity));
        Anim.SetPressed(el, false);
        Assert.True(ReferenceEquals(el.RenderTransform, Transform.Identity));
    }));

    [Fact]
    public void Helpers_GateOn_StartTheirMotion() => Sta.Run(() => WithTokens(Tokens(), () =>
    {
        var el = new Border();
        Anim.Pop(el);
        Assert.IsType<ScaleTransform>(el.RenderTransform);

        var pressed = new Border();
        Anim.SetPressed(pressed, true);
        Assert.IsType<ScaleTransform>(pressed.RenderTransform);

        var a = new Border();
        var b = new Border { Visibility = Visibility.Collapsed };
        Anim.SlideSwap(a, b, 1);
        Assert.Equal(Visibility.Visible, b.Visibility);
        Assert.IsType<TranslateTransform>(b.RenderTransform);
        Assert.Equal(24.0, ((TranslateTransform)b.RenderTransform).X);   // arrives from the end edge

        // RTL: WPF mirrors RenderTransform itself, so the incoming view must start visually to the LEFT (the end edge).
        var root = new Grid { Width = 200, Height = 100 };
        var rtlParent = new Grid { FlowDirection = FlowDirection.RightToLeft };
        root.Children.Add(rtlParent);
        var rtl = new Border { Width = 50 };
        var r2 = new Border { Width = 50 };
        rtlParent.Children.Add(rtl);
        rtlParent.Children.Add(r2);
        root.Measure(new Size(200, 100));
        root.Arrange(new Rect(0, 0, 200, 100));
        var restX = r2.TransformToAncestor(root).Transform(new Point(0, 0)).X;
        Anim.SlideSwap(rtl, r2, 1);
        root.UpdateLayout();
        var startX = r2.TransformToAncestor(root).Transform(new Point(0, 0)).X;
        Assert.Equal(restX - 24, startX, 3);
    }));

    // ---- elevation tokens -------------------------------------------------------------------------------

    [Fact]
    public void Tokens_Xaml_HasElevationSpaceRadiusAndTextStyles()
    {
        var keys = XDocument.Load(Path.Combine(SourceTree.AppDir(), "Styles", "Tokens.xaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key")).ToHashSet();
        foreach (var k in new[] { "Space48", "RadiusLarge", "Display", "Title2", "BodyStrong",
                                  "Elevation1Margin", "Elevation1Opacity", "Elevation2Margin", "Elevation2Opacity",
                                  "Elevation3Margin", "Elevation3Opacity" })
            Assert.Contains(k, keys);
    }
}
