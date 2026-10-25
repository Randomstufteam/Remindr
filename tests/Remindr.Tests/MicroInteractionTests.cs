using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Remindr.ViewModels;
using Remindr.Views.Controls;

namespace Remindr.Tests;

/// <summary>Remind UI 0.4 slice 8: micro-interactions and calendar transitions (pure numbers, gate-off end states, template wiring).</summary>
[Collection("AnimGate")] // Anim.Lookup is process-wide state
public class MicroInteractionTests
{
    private static ResourceDictionary Tokens(bool motion = true, bool visual = true)
    {
        using var s = File.OpenRead(Path.Combine(SourceTree.AppDir(), "Styles", "Animations.xaml"));
        var d = (ResourceDictionary)XamlReader.Load(s);
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

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SourceTree.AppDir() }.Concat(parts).ToArray()));

    // ---- card lift numbers -------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, false, 1.0, 0.0)]
    [InlineData(false, true, false, 1.04, -4.0)]
    [InlineData(false, true, true, 0.97, 0.0)]
    [InlineData(true, false, false, 1.0, 0.0)]
    [InlineData(true, true, false, 1.0, -2.0)]   // the full card: lifts 2px, no scale
    [InlineData(true, true, true, 0.99, 0.0)]    // pressed: 0.99
    public void TileMotion_Targets(bool subtle, bool hover, bool pressed, double scale, double y)
    {
        var (s, dy) = TileMotion.Targets(subtle, hover, pressed);
        Assert.Equal(scale, s);
        Assert.Equal(y, dy);
    }

    [Fact]
    public void SubtleHost_PointerAndPress_DriveTheLiftGrid_ButNotButtonPresses() => Sta.Run(() =>
    {
        var host = new Grid { Background = Brushes.Transparent };
        var lift = new Grid { RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } } };
        var button = new Button();
        lift.Children.Add(button);
        host.Children.Add(lift);
        TileMotion.SetSubtleHost(host, true);

        host.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Assert.True(TileMotion.GetHover(lift));

        host.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = lift });
        Assert.True(TileMotion.GetPressed(lift));
        host.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = lift });
        Assert.False(TileMotion.GetPressed(lift));

        // A press that lands on a button inside is that button's click, not a card press.
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = button });
        Assert.False(TileMotion.GetPressed(lift));

        host.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.False(TileMotion.GetHover(lift));

        // Switching the host off (compact cards) also clears a hover that was already on.
        host.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        TileMotion.SetSubtleHost(host, false);
        Assert.False(TileMotion.GetHover(lift));
    });

    [Fact]
    public void Hover_OnAFrozenTemplateTransform_DoesNotThrow() => Sta.Run(() => WithTokens(Tokens(), () =>
    {
        // A TransformGroup declared in a DataTemplate (ReminderCard's LiftRoot) arrives frozen; hovering used to crash the app.
        var group = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
        group.Freeze();
        var lift = new Grid { RenderTransform = group };
        TileMotion.SetSubtle(lift, true);
        TileMotion.SetHover(lift, true);
        TileMotion.SetPressed(lift, true);
        Assert.False(lift.RenderTransform.IsFrozen);
        Assert.NotSame(group, lift.RenderTransform);
    }));

    // ---- calendar direction ------------------------------------------------------------------------------

    [Fact]
    public void NavigationDirection_FollowsDateOrder()
    {
        var d = new DateOnly(2026, 10, 25);
        Assert.Equal(1, CalendarViewModel.NavigationDirection(d, d.AddMonths(1)));
        Assert.Equal(-1, CalendarViewModel.NavigationDirection(d, d.AddDays(-1)));
        Assert.Equal(0, CalendarViewModel.NavigationDirection(d, d));
    }

    // ---- gate off: end states at once ----------------------------------------------------------------

    private static void GateOffCases(Action<ResourceDictionary> body) => Sta.Run(() =>
    {
        body(Tokens(visual: false));
        body(Tokens(motion: false));
    });

    [Fact]
    public void GrowWhen_GateOff_JumpsToTheEndState() => GateOffCases(d => WithTokens(d, () =>
    {
        var bar = new System.Windows.Shapes.Rectangle { RenderTransform = new ScaleTransform(1, 0) };
        Anim.SetGrowWhen(bar, true);
        Assert.Equal(1.0, ((ScaleTransform)bar.RenderTransform).ScaleY);
        Anim.SetGrowWhen(bar, false);
        Assert.Equal(0.0, ((ScaleTransform)bar.RenderTransform).ScaleY);
    }));

    [Fact]
    public void NudgeWhen_GateOff_MovesTwoPixelsAtOnce() => GateOffCases(d => WithTokens(d, () =>
    {
        var icon = new Border();
        Anim.SetNudgeWhen(icon, true);
        Assert.Equal(2.0, ((TranslateTransform)icon.RenderTransform).X);
        Anim.SetNudgeWhen(icon, false);
        Assert.Equal(0.0, ((TranslateTransform)icon.RenderTransform).X);
    }));

    [Fact]
    public void RingWhen_GateOff_ShowsAndHidesAtOnce() => GateOffCases(d => WithTokens(d, () =>
    {
        var ring = new Border { Opacity = 0 };
        Anim.SetRingWhen(ring, true);
        Assert.Equal(1.0, ring.Opacity);
        Assert.True(ReferenceEquals(ring.RenderTransform, Transform.Identity));
        Anim.SetRingWhen(ring, false);
        Assert.Equal(0.0, ring.Opacity);
    }));

    [Fact]
    public void SlidePill_PutsThePillOnTheIndexedCell()
    {
        GateOffCases(d => WithTokens(d, () =>
        {
            var pill = new Border();
            Anim.SlidePill(pill, 2, 80, animate: true);
            Assert.Equal(160.0, ((TranslateTransform)pill.RenderTransform).X);
            Anim.SlidePill(pill, 0, 80, animate: true);
            Assert.Equal(0.0, ((TranslateTransform)pill.RenderTransform).X);
        }));
        // Gate on but not animating (first layout): also a jump.
        Sta.Run(() => WithTokens(Tokens(), () =>
        {
            var pill = new Border();
            Anim.SlidePill(pill, 1, 90, animate: false);
            Assert.Equal(90.0, ((TranslateTransform)pill.RenderTransform).X);
        }));
    }

    [Fact]
    public void FadeSwap_GateOff_LeavesTheTextAtFullOpacity() => GateOffCases(d => WithTokens(d, () =>
    {
        var title = new TextBlock { Text = "November 2026" };
        Anim.FadeSwap(title);
        Assert.Equal(1.0, title.Opacity);
    }));

    [Fact]
    public void Rest_CancelsAMidSwapState() => Sta.Run(() => WithTokens(Tokens(), () =>
    {
        var a = new Border();
        var b = new Border { Visibility = Visibility.Collapsed };
        Anim.SlideSwap(a, b, 1);
        Anim.Rest(b);
        Anim.Rest(a);
        Assert.Equal(1.0, b.Opacity);
        Assert.True(ReferenceEquals(b.RenderTransform, Transform.Identity));
        Assert.Equal(1.0, a.Opacity);
    }));

    // ---- wiring in the XAML ------------------------------------------------------------------------------

    [Fact]
    public void GlassPill_PressScales_AndIconActionAndNavButtonInheritIt()
    {
        var glass = Read("Styles", "Glass.xaml");
        var pill = glass.IndexOf("x:Key=\"GlassPill\"", StringComparison.Ordinal);
        Assert.True(pill > 0);
        Assert.Contains("ctl:Anim.PressScale", glass[pill..(pill + 400)]);
        Assert.Contains("x:Key=\"IconAction\" TargetType=\"Button\" BasedOn=\"{StaticResource GlassPill}\"", Read("Views", "Controls", "ReminderCard.xaml"));
        Assert.Contains("x:Key=\"NavButton\" TargetType=\"Button\" BasedOn=\"{StaticResource GlassPill}\"", Read("Views", "Pages", "CalendarPage.xaml"));
    }

    [Fact]
    public void ReminderCard_LiftsInSubtleMode_ExceptCompact()
    {
        var xaml = Read("Views", "Controls", "ReminderCard.xaml");
        Assert.Contains("ctl:TileMotion.SubtleHost=\"True\"", xaml);
        Assert.Contains("ctl:TileMotion.Subtle=\"True\"", xaml);
        Assert.Contains("x:Name=\"LiftRoot\"", xaml);
        Assert.Contains("Property=\"ctl:TileMotion.SubtleHost\" Value=\"False\"", xaml);
    }

    [Fact]
    public void NavRail_IndicatorGrowsAndIconNudges()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.Contains("ctl:Anim.GrowWhen=\"{Binding IsActive", xaml);
        Assert.Contains("ctl:Anim.NudgeWhen=\"{Binding IsMouseOver", xaml);
    }

    [Fact]
    public void Calendar_UsesOneSlidingPill_AndRingsAndSwaps()
    {
        var xaml = Read("Views", "Pages", "CalendarPage.xaml");
        Assert.Contains("x:Name=\"SegmentPill\"", xaml);
        Assert.Contains("ctl:Anim.RingWhen=\"{Binding IsSelected}\"", xaml);
        Assert.Contains("x:Name=\"MonthGridOut\"", xaml);
        Assert.Contains("x:Name=\"WeekColumnsOut\"", xaml);
        // The checked segment no longer paints its own accent fill.
        var seg = xaml.IndexOf("x:Key=\"SegmentButton\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</Style>", seg, StringComparison.Ordinal);
        var checkedTrigger = xaml[seg..end];
        checkedTrigger = checkedTrigger[checkedTrigger.IndexOf("Property=\"IsChecked\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Value=\"{DynamicResource AccentFillColorDefaultBrush}\"", checkedTrigger);
    }
}
