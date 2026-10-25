using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Remindr.Views.Controls;

/// <summary>
/// Shared motion helpers. Every duration comes from Styles/Animations.xaml (DurFast/DurNormal/DurSlow, StaggerMs) and
/// everything is skipped when MotionEnabled is false (Windows animations off). Resting values are always exact
/// (opacity 1, identity transform): animations are cleared when they finish.
/// </summary>
public static class Anim
{
    // ---- tokens ------------------------------------------------------------------------------------

    /// <summary>Where tokens come from (the application resources). Tests replace it; null result = token missing.</summary>
    public static Func<string, object?> Lookup { get; set; } = key => Application.Current?.TryFindResource(key);

    public static bool MotionEnabled => Lookup("MotionEnabled") is not false;

    /// <summary>
    /// The single effects gate for Remind UI 0.4: MotionEnabled (Windows animations) AND VisualEffectsEnabled (--no-effects now,
    /// "Reduce visual effects" in Phase 6.9). Read it when an effect starts and never cache it. When false an effect writes its end state directly.
    /// </summary>
    public static bool EffectsEnabled => MotionEnabled && Lookup("VisualEffectsEnabled") is not false;

    public static Duration Dur(string key) =>
        Lookup(key) is Duration d ? d : new Duration(TimeSpan.FromMilliseconds(200));

    public static double StaggerMs =>
        Lookup("StaggerMs") is double d ? d : 40;

    public static IEasingFunction EaseOut =>
        Lookup("EaseOut") as IEasingFunction ?? new CubicEase { EasingMode = EasingMode.EaseOut };

    public static IEasingFunction EasePop =>
        Lookup("EasePop") as IEasingFunction ?? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

    public static IEasingFunction EaseInOut =>
        Lookup("EaseInOut") as IEasingFunction ?? new CubicEase { EasingMode = EasingMode.EaseInOut };

    public static double StaggerRevealMs =>
        Lookup("StaggerRevealMs") is double d ? d : 90;

    private static bool IsZero(Duration d) => d.HasTimeSpan && d.TimeSpan == TimeSpan.Zero;

    // ---- Remind UI 0.4 helpers (all behind EffectsEnabled; transform and opacity only) ----------------------

    /// <summary>Scale 0.85 to 1 from the centre with the EasePop overshoot over DurEnter. Gate off: nothing happens (the element is already at rest).</summary>
    public static void Pop(UIElement el, TimeSpan delay = default)
    {
        var dur = Dur("DurEnter");
        if (!EffectsEnabled || IsZero(dur)) return;

        var version = (int)el.GetValue(VersionProperty) + 1;
        el.SetValue(VersionProperty, version);

        var scale = new ScaleTransform(0.85, 0.85);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        var a = new DoubleAnimation(0.85, 1, dur) { EasingFunction = EasePop, BeginTime = delay };
        a.Completed += (_, _) =>
        {
            if ((int)el.GetValue(VersionProperty) != version || !ReferenceEquals(el.RenderTransform, scale)) return;
            el.RenderTransform = Transform.Identity;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a.Clone());
    }

    private const double SwapDistance = 24;

    /// <summary>
    /// Swaps two stacked views. <paramref name="direction"/> +1 = forward (old content leaves toward the start edge, new content
    /// arrives from the end edge), -1 = back. Right-to-left needs no sign flip: WPF already mirrors a RenderTransform there, so
    /// +X points at the end edge either way. Old slides out 24 DIPs and fades (DurExit), new fades and slides in (DurEnter). At rest the outgoing element is Collapsed with identity transform and opacity 1, the incoming
    /// one Visible. Gate off: that end state is set at once.
    /// </summary>
    public static void SlideSwap(UIElement outgoing, UIElement incoming, int direction)
    {
        var exit = Dur("DurExit");
        var enter = Dur("DurEnter");
        var sign = direction < 0 ? -1.0 : 1.0;

        var version = (int)outgoing.GetValue(VersionProperty) + 1;
        outgoing.SetValue(VersionProperty, version);
        var inVersion = (int)incoming.GetValue(VersionProperty) + 1;
        incoming.SetValue(VersionProperty, inVersion);

        void Rest()
        {
            outgoing.BeginAnimation(UIElement.OpacityProperty, null);
            outgoing.Opacity = 1;
            outgoing.RenderTransform = Transform.Identity;
            outgoing.Visibility = Visibility.Collapsed;
        }
        void RestIn()
        {
            incoming.BeginAnimation(UIElement.OpacityProperty, null);
            incoming.Opacity = 1;
            incoming.RenderTransform = Transform.Identity;
        }

        incoming.Visibility = Visibility.Visible;
        if (!EffectsEnabled || (IsZero(exit) && IsZero(enter)))
        {
            Rest();
            RestIn();
            return;
        }

        var outMove = new TranslateTransform();
        outgoing.RenderTransform = outMove;
        outMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, -sign * SwapDistance, exit) { EasingFunction = EaseInOut });
        var fadeOut = new DoubleAnimation(1, 0, exit) { EasingFunction = EaseInOut };
        fadeOut.Completed += (_, _) =>
        {
            if ((int)outgoing.GetValue(VersionProperty) == version) Rest();
        };
        outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        var inMove = new TranslateTransform(sign * SwapDistance, 0);
        incoming.RenderTransform = inMove;
        incoming.Opacity = 0;
        inMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(sign * SwapDistance, 0, enter) { EasingFunction = EaseOut });
        var fadeIn = new DoubleAnimation(0, 1, enter) { EasingFunction = EaseOut };
        fadeIn.Completed += (_, _) =>
        {
            if ((int)incoming.GetValue(VersionProperty) == inVersion) RestIn();
        };
        incoming.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    // ---- attached: PressScale (scale 0.96 while the left button is held) -------------------------------------

    public static readonly DependencyProperty PressScaleProperty = DependencyProperty.RegisterAttached(
        "PressScale", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnPressScaleChanged));

    public static bool GetPressScale(DependencyObject d) => (bool)d.GetValue(PressScaleProperty);
    public static void SetPressScale(DependencyObject d, bool v) => d.SetValue(PressScaleProperty, v);

    private const double PressedScale = 0.96;

    private static void OnPressScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.PreviewMouseLeftButtonDown -= OnPressDown;
        el.PreviewMouseLeftButtonUp -= OnPressUp;
        el.MouseLeave -= OnPressUp;
        el.LostMouseCapture -= OnPressUp;
        if (!(bool)e.NewValue) return;
        el.PreviewMouseLeftButtonDown += OnPressDown;
        el.PreviewMouseLeftButtonUp += OnPressUp;
        el.MouseLeave += OnPressUp;
        el.LostMouseCapture += OnPressUp;
    }

    private static void OnPressDown(object sender, RoutedEventArgs e) => SetPressed((UIElement)sender, true);
    private static void OnPressUp(object sender, RoutedEventArgs e) => SetPressed((UIElement)sender, false);

    /// <summary>Scales to 0.96 (pressed) or back to 1 over DurFast. Gate off: no-op, the element stays at rest.</summary>
    public static void SetPressed(UIElement el, bool pressed)
    {
        var dur = Dur("DurFast");
        if (!EffectsEnabled || IsZero(dur))
        {
            // A release must still leave an exact resting transform if the gate turned off while pressed.
            if (!pressed && el.RenderTransform is ScaleTransform { IsFrozen: false } held)
            {
                held.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                held.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                el.RenderTransform = Transform.Identity;
            }
            return;
        }
        var scale = el.RenderTransform as ScaleTransform;
        if (scale is null || scale.IsFrozen)
        {
            if (!pressed) return;
            // Never replace someone else's transform (a FadeInOnLoad slide, TileMotion); PressScale only owns a bare element.
            if (el.RenderTransform is { } other && !ReferenceEquals(other, Transform.Identity) && !other.Value.IsIdentity) return;
            scale = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = scale;
        }
        var to = pressed ? PressedScale : 1.0;
        var a = new DoubleAnimation(to, dur) { EasingFunction = EaseOut };
        if (!pressed)
            a.Completed += (_, _) =>
            {
                if (!ReferenceEquals(el.RenderTransform, scale)) return;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                el.RenderTransform = Transform.Identity;
            };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a.Clone());
    }

    // ---- reminder card motion (slice 7): attached behaviours bound to view-model flags ------------------------
    // Each reacts to a change only once the element is loaded (so the initial binding just shows the end state) and only
    // while EffectsEnabled; otherwise the end state is written directly.

    private static readonly DependencyProperty MotionVersionProperty = DependencyProperty.RegisterAttached(
        "MotionVersion", typeof(int), typeof(Anim), new PropertyMetadata(0));

    private static int NextMotionVersion(DependencyObject d)
    {
        var v = (int)d.GetValue(MotionVersionProperty) + 1;
        d.SetValue(MotionVersionProperty, v);
        return v;
    }

    private static bool CanAnimate(FrameworkElement el, Duration dur) => el.IsLoaded && EffectsEnabled && !IsZero(dur);

    /// <summary>Pops when the bound flag becomes true (the complete tick, the Important flag).</summary>
    public static readonly DependencyProperty PopWhenProperty = DependencyProperty.RegisterAttached(
        "PopWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) =>
        {
            if (d is FrameworkElement el && (bool)e.NewValue && el.IsLoaded) Pop(el);
        }));
    public static bool GetPopWhen(DependencyObject d) => (bool)d.GetValue(PopWhenProperty);
    public static void SetPopWhen(DependencyObject d, bool v) => d.SetValue(PopWhenProperty, v);

    /// <summary>Cross-fade: opacity follows the flag (true = 1). Put Opacity="0" on the element for an initial false.</summary>
    public static readonly DependencyProperty ShowWhenProperty = DependencyProperty.RegisterAttached(
        "ShowWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) => FadeTo(d, (bool)e.NewValue ? 1 : 0)));
    public static bool GetShowWhen(DependencyObject d) => (bool)d.GetValue(ShowWhenProperty);
    public static void SetShowWhen(DependencyObject d, bool v) => d.SetValue(ShowWhenProperty, v);

    /// <summary>Cross-fade: opacity follows the opposite of the flag (true = 0).</summary>
    public static readonly DependencyProperty HideWhenProperty = DependencyProperty.RegisterAttached(
        "HideWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) => FadeTo(d, (bool)e.NewValue ? 0 : 1)));
    public static bool GetHideWhen(DependencyObject d) => (bool)d.GetValue(HideWhenProperty);
    public static void SetHideWhen(DependencyObject d, bool v) => d.SetValue(HideWhenProperty, v);

    private static void FadeTo(DependencyObject d, double to)
    {
        if (d is not FrameworkElement el) return;
        var version = NextMotionVersion(el);
        var dur = Dur("DurFast");
        if (!CanAnimate(el, dur))
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = to;
            return;
        }
        var a = new DoubleAnimation(to, dur) { EasingFunction = EaseOut };
        a.Completed += (_, _) =>
        {
            if ((int)el.GetValue(MotionVersionProperty) != version) return;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = to;
        };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>Card deleted: fade and slide 24 DIPs toward the trailing edge (DurExit). false restores it (a failed delete).</summary>
    public static readonly DependencyProperty ExitWhenProperty = DependencyProperty.RegisterAttached(
        "ExitWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) => Leave(d, (bool)e.NewValue, 24, 0, collapse: false)));
    public static bool GetExitWhen(DependencyObject d) => (bool)d.GetValue(ExitWhenProperty);
    public static void SetExitWhen(DependencyObject d, bool v) => d.SetValue(ExitWhenProperty, v);

    /// <summary>
    /// Card just completed and about to move lists: fade and slide 8 DIPs down (DurExit), then Collapsed, so the cards below close up
    /// even when a newer tick postpones the list refresh (several cards ticked quickly). false restores it (untick in time).
    /// </summary>
    public static readonly DependencyProperty SettleWhenProperty = DependencyProperty.RegisterAttached(
        "SettleWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) => Leave(d, (bool)e.NewValue, 0, 8, collapse: true)));
    public static bool GetSettleWhen(DependencyObject d) => (bool)d.GetValue(SettleWhenProperty);
    public static void SetSettleWhen(DependencyObject d, bool v) => d.SetValue(SettleWhenProperty, v);

    private static void Leave(DependencyObject d, bool on, double dx, double dy, bool collapse)
    {
        if (d is not FrameworkElement el) return;
        var version = NextMotionVersion(el);
        var dur = Dur("DurExit");
        var toOpacity = on ? 0.0 : 1.0;
        var toX = on ? dx : 0.0;
        var toY = on ? dy : 0.0;

        void End()
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = toOpacity;
            el.RenderTransform = on ? new TranslateTransform(toX, toY) : Transform.Identity;
            if (collapse) el.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        }

        if (!CanAnimate(el, dur))
        {
            End();
            return;
        }

        if (collapse && !on) el.Visibility = Visibility.Visible;
        var move = el.RenderTransform as TranslateTransform;
        if (move is null || move.IsFrozen)
        {
            move = new TranslateTransform();
            el.RenderTransform = move;
        }
        var fade = new DoubleAnimation(toOpacity, dur) { EasingFunction = EaseInOut };
        fade.Completed += (_, _) =>
        {
            if ((int)el.GetValue(MotionVersionProperty) == version) End();
        };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(toX, dur) { EasingFunction = EaseInOut });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toY, dur) { EasingFunction = EaseInOut });
    }

    /// <summary>
    /// Strike line over a single-line title (a Rectangle in the same Grid as the title TextBlock): ScaleX 0 to 1 from the leading edge
    /// (DurNormal). A wrapped title cannot use one line, so it falls back to TextDecorations, set at once.
    /// </summary>
    public static readonly DependencyProperty StrikeWhenProperty = DependencyProperty.RegisterAttached(
        "StrikeWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnStrikeChanged));
    public static bool GetStrikeWhen(DependencyObject d) => (bool)d.GetValue(StrikeWhenProperty);
    public static void SetStrikeWhen(DependencyObject d, bool v) => d.SetValue(StrikeWhenProperty, v);

    private static readonly DependencyProperty StrikeHookedProperty = DependencyProperty.RegisterAttached(
        "StrikeHooked", typeof(bool), typeof(Anim), new PropertyMetadata(false));
    private static readonly DependencyProperty StrikeAnimatingProperty = DependencyProperty.RegisterAttached(
        "StrikeAnimating", typeof(bool), typeof(Anim), new PropertyMetadata(false));

    private static TextBlock? StrikeTitle(FrameworkElement line) =>
        (line.Parent as Panel)?.Children.OfType<TextBlock>().FirstOrDefault();

    private static bool IsWrapped(TextBlock tb) => tb.ActualHeight > tb.FontSize * 2;

    private static void SetStrikeScale(FrameworkElement line, double x)
    {
        if (line.RenderTransform is ScaleTransform { IsFrozen: false } s)
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.ScaleX = x;
        }
        else
            line.RenderTransform = new ScaleTransform(x, 1);
    }

    /// <summary>Writes the strike end state with no animation.</summary>
    private static void SyncStrike(FrameworkElement line)
    {
        line.SetValue(StrikeAnimatingProperty, false);
        var on = GetStrikeWhen(line);
        var tb = StrikeTitle(line);
        var wrapped = tb is not null && line.IsLoaded && IsWrapped(tb);
        if (tb is not null)
            tb.SetValue(TextBlock.TextDecorationsProperty, on && wrapped ? TextDecorations.Strikethrough : DependencyProperty.UnsetValue);
        SetStrikeScale(line, on && !wrapped ? 1 : 0);
    }

    private static void OnStrikeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement line) return;
        if (!(bool)line.GetValue(StrikeHookedProperty))
        {
            line.SetValue(StrikeHookedProperty, true);
            line.Loaded += (_, _) => SyncStrike(line);
            line.SizeChanged += (_, args) =>
            {
                if (args.WidthChanged && !(bool)line.GetValue(StrikeAnimatingProperty)) SyncStrike(line);
            };
        }

        var on = (bool)e.NewValue;
        var dur = Dur("DurNormal");
        var tb = StrikeTitle(line);
        if (!CanAnimate(line, dur) || tb is null || IsWrapped(tb))
        {
            SyncStrike(line);
            return;
        }

        var version = NextMotionVersion(line);
        line.SetValue(StrikeAnimatingProperty, true);
        tb.ClearValue(TextBlock.TextDecorationsProperty);
        var scale = new ScaleTransform(on ? 0 : 1, 1);
        line.RenderTransform = scale;
        var a = new DoubleAnimation(on ? 1 : 0, dur) { EasingFunction = EaseOut };
        a.Completed += (_, _) =>
        {
            if ((int)line.GetValue(MotionVersionProperty) == version) SyncStrike(line);
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
    }

    /// <summary>
    /// Scroll into view and glow once: the view model bumps a token (> 0) to request it. The request is dropped if the element does not
    /// load within 2 seconds (a card inside a collapsed group must not glow whenever it is expanded later). The overlay keeps Opacity="0".
    /// </summary>
    public static readonly DependencyProperty GlowOnProperty = DependencyProperty.RegisterAttached(
        "GlowOn", typeof(int), typeof(Anim), new PropertyMetadata(0, OnGlowChanged));
    public static int GetGlowOn(DependencyObject d) => (int)d.GetValue(GlowOnProperty);
    public static void SetGlowOn(DependencyObject d, int v) => d.SetValue(GlowOnProperty, v);

    private static readonly DependencyProperty GlowRequestedAtProperty = DependencyProperty.RegisterAttached(
        "GlowRequestedAt", typeof(long), typeof(Anim), new PropertyMetadata(0L));

    private const long GlowRequestTtlMs = 2000;

    private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || (int)e.NewValue <= 0) return;
        el.SetValue(GlowRequestedAtProperty, Environment.TickCount64);
        if (el.IsLoaded) { ScheduleGlow(el); return; }
        RoutedEventHandler? onLoaded = null;
        onLoaded = (_, _) =>
        {
            el.Loaded -= onLoaded;
            if (Environment.TickCount64 - (long)el.GetValue(GlowRequestedAtProperty) <= GlowRequestTtlMs) ScheduleGlow(el);
        };
        el.Loaded += onLoaded;
    }

    private static void ScheduleGlow(FrameworkElement el) =>
        el.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (!el.IsLoaded) return;
            el.BringIntoView();
            var rise = Dur("DurEnter");
            var fall = Dur("DurHero");
            if (!EffectsEnabled || IsZero(rise) || IsZero(fall)) return;
            var version = NextMotionVersion(el);
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(rise.TimeSpan), EaseOut));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(rise.TimeSpan + fall.TimeSpan), EaseInOut));
            frames.Completed += (_, _) =>
            {
                if ((int)el.GetValue(MotionVersionProperty) != version) return;
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = 0;
            };
            el.BeginAnimation(UIElement.OpacityProperty, frames);
        });

    // ---- slice 8: nav rail, selection ring, sliding pill, title cross-fade --------------------------------------------

    /// <summary>Scale an element's Y (or X) between 0 and 1 around its centre: true grows with the EasePop overshoot (DurEnter), false shrinks (DurFast).</summary>
    private static void GrowTo(FrameworkElement el, bool on, bool vertical)
    {
        var version = NextMotionVersion(el);
        var dur = Dur(on ? "DurEnter" : "DurFast");
        var to = on ? 1.0 : 0.0;
        var prop = vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;

        var scale = el.RenderTransform as ScaleTransform;
        if (scale is null || scale.IsFrozen)
        {
            scale = new ScaleTransform(vertical ? 1 : (on ? 0 : 1), vertical ? (on ? 0 : 1) : 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = scale;
        }
        scale.BeginAnimation(prop, null);
        if (!CanAnimate(el, dur))
        {
            scale.SetValue(prop, to);
            return;
        }
        var a = new DoubleAnimation(to, dur) { EasingFunction = on ? EasePop : EaseOut };
        a.Completed += (_, _) =>
        {
            if ((int)el.GetValue(MotionVersionProperty) != version) return;
            scale.BeginAnimation(prop, null);
            scale.SetValue(prop, to);
        };
        scale.BeginAnimation(prop, a);
    }

    /// <summary>The nav rail's selection bar: grows ScaleY 0 to 1 when the item becomes active. The element starts at ScaleY 0 in its template.</summary>
    public static readonly DependencyProperty GrowWhenProperty = DependencyProperty.RegisterAttached(
        "GrowWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) =>
        {
            if (d is FrameworkElement el) GrowTo(el, (bool)e.NewValue, vertical: true);
        }));
    public static bool GetGrowWhen(DependencyObject d) => (bool)d.GetValue(GrowWhenProperty);
    public static void SetGrowWhen(DependencyObject d, bool v) => d.SetValue(GrowWhenProperty, v);

    /// <summary>The selected day's ring: appears with scale 0.9 to 1 (EasePop) plus a fade; it disappears with a quick fade. Keep Opacity="0" on it.</summary>
    public static readonly DependencyProperty RingWhenProperty = DependencyProperty.RegisterAttached(
        "RingWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnRingChanged));
    public static bool GetRingWhen(DependencyObject d) => (bool)d.GetValue(RingWhenProperty);
    public static void SetRingWhen(DependencyObject d, bool v) => d.SetValue(RingWhenProperty, v);

    private static void OnRingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        var on = (bool)e.NewValue;
        var version = NextMotionVersion(el);
        var dur = Dur("DurEnter");
        if (el.RenderTransform is ScaleTransform { IsFrozen: false } old)
        {
            old.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            old.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        if (!on || !CanAnimate(el, dur))
        {
            // Off, or no motion: the end state at once (off also fades quickly when it can).
            if (!on) { FadeTo(el, 0); }
            else
            {
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = 1;
            }
            el.RenderTransform = Transform.Identity;
            return;
        }
        var scale = new ScaleTransform(0.9, 0.9);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        var grow = new DoubleAnimation(0.9, 1, dur) { EasingFunction = EasePop };
        grow.Completed += (_, _) =>
        {
            if ((int)el.GetValue(MotionVersionProperty) != version) return;
            el.RenderTransform = Transform.Identity;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
        var fade = new DoubleAnimation(0, 1, Dur("DurFast")) { EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            if ((int)el.GetValue(MotionVersionProperty) != version) return;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
        };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>While true the element sits <see cref="NudgeDistance"/> DIPs toward the end edge (a nav icon toward its label), DurFast. WPF mirrors it under RTL.</summary>
    public static readonly DependencyProperty NudgeWhenProperty = DependencyProperty.RegisterAttached(
        "NudgeWhen", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not FrameworkElement el) return;
            var version = NextMotionVersion(el);
            var dur = Dur("DurFast");
            var to = (bool)e.NewValue ? NudgeDistance : 0.0;
            var move = el.RenderTransform as TranslateTransform;
            if (move is null || move.IsFrozen)
            {
                move = new TranslateTransform();
                el.RenderTransform = move;
            }
            move.BeginAnimation(TranslateTransform.XProperty, null);
            if (!CanAnimate(el, dur))
            {
                move.X = to;
                return;
            }
            var a = new DoubleAnimation(to, dur) { EasingFunction = EaseOut };
            a.Completed += (_, _) =>
            {
                if ((int)el.GetValue(MotionVersionProperty) != version) return;
                move.BeginAnimation(TranslateTransform.XProperty, null);
                move.X = to;
            };
            move.BeginAnimation(TranslateTransform.XProperty, a);
        }));
    public static bool GetNudgeWhen(DependencyObject d) => (bool)d.GetValue(NudgeWhenProperty);
    public static void SetNudgeWhen(DependencyObject d, bool v) => d.SetValue(NudgeWhenProperty, v);

    public const double NudgeDistance = 2;

    /// <summary>
    /// Slides the single selection pill of a segmented control: TranslateX to <paramref name="index"/> x <paramref name="cellWidth"/>
    /// (DurEnter, EaseInOut). <paramref name="animate"/> false, or the gate off, jumps there. Left-to-right coordinates: WPF mirrors the
    /// transform under RTL, where the segments are mirrored too.
    /// </summary>
    public static void SlidePill(FrameworkElement pill, int index, double cellWidth, bool animate)
    {
        var version = NextMotionVersion(pill);
        var to = index * cellWidth;
        var dur = Dur("DurEnter");
        var move = pill.RenderTransform as TranslateTransform;
        if (move is null || move.IsFrozen)
        {
            move = new TranslateTransform();
            pill.RenderTransform = move;
        }
        move.BeginAnimation(TranslateTransform.XProperty, null);
        if (!animate || !CanAnimate(pill, dur))
        {
            move.X = to;
            return;
        }
        var a = new DoubleAnimation(to, dur) { EasingFunction = EaseInOut };
        a.Completed += (_, _) =>
        {
            if ((int)pill.GetValue(MotionVersionProperty) != version) return;
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.X = to;
        };
        move.BeginAnimation(TranslateTransform.XProperty, a);
    }

    /// <summary>The text just changed (a calendar title): fade the new text in from 0 over DurNormal. Gate off: nothing, it simply shows the new text.</summary>
    public static void FadeSwap(UIElement el)
    {
        var dur = Dur("DurNormal");
        if (!EffectsEnabled || IsZero(dur)) return;
        var version = (int)el.GetValue(VersionProperty) + 1;
        el.SetValue(VersionProperty, version);
        var fade = new DoubleAnimation(0, 1, dur) { EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            if ((int)el.GetValue(VersionProperty) != version) return;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
        };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Puts an element back at rest (opacity 1, no transform, animations cancelled) and cancels any pending completion of an earlier swap.</summary>
    public static void Rest(UIElement el)
    {
        el.SetValue(VersionProperty, (int)el.GetValue(VersionProperty) + 1);
        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.Opacity = 1;
        el.RenderTransform = Transform.Identity;
    }

    // ---- slice 9: the slow float of an empty-state picture -----------------------------------------------------------

    /// <summary>
    /// Raised by whoever flips the effects gate at runtime (Phase 6.9 "Reduce visual effects"), so looping decorations such as
    /// <see cref="FloatProperty"/> stop at once instead of at their next visibility change.
    /// </summary>
    public static event EventHandler? EffectsChanged;
    public static void NotifyEffectsChanged() => EffectsChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// A slow looping float (TranslateY +-4 DIPs, one full cycle per DurFloat) on an element with no other RenderTransform. It runs only
    /// while the element is loaded and visible, the gate is on and its window is not minimised, and is removed (back at Y 0) otherwise.
    /// </summary>
    public static readonly DependencyProperty FloatProperty = DependencyProperty.RegisterAttached(
        "Float", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnFloatChanged));
    public static bool GetFloat(DependencyObject d) => (bool)d.GetValue(FloatProperty);
    public static void SetFloat(DependencyObject d, bool v) => d.SetValue(FloatProperty, v);

    public const double FloatAmplitude = 4;

    private sealed class FloatState
    {
        public readonly TranslateTransform Move = new();
        public AnimationClock? Clock;
        public Window? Window;
        public EventHandler? OnChanged;
    }

    private static readonly DependencyProperty FloatStateProperty = DependencyProperty.RegisterAttached(
        "FloatState", typeof(FloatState), typeof(Anim), new PropertyMetadata(null));

    /// <summary>True when the float loop should be running for this element right now.</summary>
    public static bool ShouldFloat(FrameworkElement el, Window? window)
    {
        var dur = Dur("DurFloat");
        return el.IsLoaded && el.IsVisible && EffectsEnabled && dur.HasTimeSpan && dur.TimeSpan > TimeSpan.Zero
               && window?.WindowState != WindowState.Minimized;
    }

    private static void OnFloatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        el.Loaded -= FloatLoaded;
        el.Unloaded -= FloatUnloaded;
        el.IsVisibleChanged -= FloatVisibleChanged;
        if ((bool)e.NewValue)
        {
            el.Loaded += FloatLoaded;
            el.Unloaded += FloatUnloaded;
            el.IsVisibleChanged += FloatVisibleChanged;
            if (el.IsLoaded) FloatLoaded(el, new RoutedEventArgs());
        }
        else
        {
            FloatUnloaded(el, new RoutedEventArgs());
        }
    }

    private static FloatState StateOf(FrameworkElement el)
    {
        if (el.GetValue(FloatStateProperty) is FloatState s) return s;
        s = new FloatState();
        el.SetValue(FloatStateProperty, s);
        return s;
    }

    private static void FloatLoaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        var s = StateOf(el);
        if (s.OnChanged is null)
        {
            s.OnChanged = (_, _) => UpdateFloat(el);
            EffectsChanged += s.OnChanged;
            s.Window = Window.GetWindow(el);
            if (s.Window is not null) s.Window.StateChanged += s.OnChanged;
        }
        UpdateFloat(el);
    }

    private static void FloatUnloaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        if (el.GetValue(FloatStateProperty) is not FloatState s) return;
        if (s.OnChanged is not null)
        {
            EffectsChanged -= s.OnChanged;
            if (s.Window is not null) s.Window.StateChanged -= s.OnChanged;
            s.OnChanged = null;
            s.Window = null;
        }
        StopFloat(s);
    }

    private static void FloatVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateFloat((FrameworkElement)sender);

    private static void StopFloat(FloatState s)
    {
        if (s.Clock is null) return;
        s.Move.ApplyAnimationClock(TranslateTransform.YProperty, null);
        s.Move.Y = 0;
        s.Clock = null;
    }

    private static void UpdateFloat(FrameworkElement el)
    {
        var s = StateOf(el);
        if (!ShouldFloat(el, s.Window)) { StopFloat(s); return; }
        if (s.Clock is not null) return;
        // One full cycle takes DurFloat: -4 to +4 over half of it, then back. BeginTime puts the start at the middle (Y 0), so nothing jumps.
        var half = TimeSpan.FromTicks(Dur("DurFloat").TimeSpan.Ticks / 2);
        var a = new DoubleAnimation(-FloatAmplitude, FloatAmplitude, half)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromTicks(-half.Ticks / 2),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Timeline.SetDesiredFrameRate(a, 30);   // a 6 s drift looks the same at 30 fps and keeps GPU use down
        el.RenderTransform = s.Move;
        s.Clock = a.CreateClock();
        s.Move.ApplyAnimationClock(TranslateTransform.YProperty, s.Clock);
    }

    // ---- attached: fade (and slide up) when the element loads ------------------------------------------

    public static readonly DependencyProperty FadeInOnLoadProperty = DependencyProperty.RegisterAttached(
        "FadeInOnLoad", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnFadeInOnLoadChanged));

    public static bool GetFadeInOnLoad(DependencyObject d) => (bool)d.GetValue(FadeInOnLoadProperty);
    public static void SetFadeInOnLoad(DependencyObject d, bool v) => d.SetValue(FadeInOnLoadProperty, v);

    /// <summary>How far (DIPs) the element slides up while fading in. 0 = fade only.</summary>
    public static readonly DependencyProperty SlideProperty = DependencyProperty.RegisterAttached(
        "Slide", typeof(double), typeof(Anim), new PropertyMetadata(12.0));

    public static double GetSlide(DependencyObject d) => (double)d.GetValue(SlideProperty);
    public static void SetSlide(DependencyObject d, double v) => d.SetValue(SlideProperty, v);

    private static readonly DependencyProperty VersionProperty = DependencyProperty.RegisterAttached(
        "Version", typeof(int), typeof(Anim), new PropertyMetadata(0));

    private static void OnFadeInOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.Loaded -= OnLoaded;
        if ((bool)e.NewValue) fe.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        FadeIn(fe, StaggerIndex(fe), GetSlide(fe));
    }

    /// <summary>Position of the element among its siblings (the item's slot inside the nearest panel).</summary>
    public static int StaggerIndex(DependencyObject element)
    {
        DependencyObject child = element;
        while (VisualTreeHelper.GetParent(child) is { } parent)
        {
            if (parent is Panel panel) return Math.Max(0, panel.Children.IndexOf((UIElement)child));
            child = parent;
        }
        return 0;
    }

    /// <summary>Fade 0 to 1 and slide up <paramref name="slide"/> DIPs, delayed by index x StaggerMs (index capped at 8 items).</summary>
    public static void FadeIn(UIElement el, int index, double slide = 12)
    {
        var dur = Dur("DurNormal");
        if (!MotionEnabled || IsZero(dur)) return;

        var version = (int)el.GetValue(VersionProperty) + 1;
        el.SetValue(VersionProperty, version);

        var delay = TimeSpan.FromMilliseconds(Math.Clamp(index, 0, 7) * StaggerMs);

        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, dur) { BeginTime = delay, EasingFunction = EaseOut };

        TranslateTransform? move = null;
        if (slide != 0)
        {
            move = new TranslateTransform(0, slide);
            el.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(slide, 0, dur) { BeginTime = delay, EasingFunction = EaseOut });
        }

        fade.Completed += (_, _) =>
        {
            if ((int)el.GetValue(VersionProperty) != version) return; // restarted meanwhile
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
            if (move is not null && ReferenceEquals(el.RenderTransform, move))
                el.RenderTransform = Transform.Identity;
        };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ---- shake (wrong PIN) ---------------------------------------------------------------------------

    /// <summary>Horizontal shake: 0, -10, 10, -6, 6, 0 over DurShake (300ms).</summary>
    public static void Shake(TranslateTransform t)
    {
        var dur = Dur("DurShake");
        if (!MotionEnabled || IsZero(dur)) return;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = dur };
        double[] xs = { 0, -10, 10, -6, 6, 0 };
        for (var i = 0; i < xs.Length; i++)
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(xs[i], KeyTime.FromPercent(i / (double)(xs.Length - 1))));
        frames.Completed += (_, _) => { t.BeginAnimation(TranslateTransform.XProperty, null); t.X = 0; };
        t.BeginAnimation(TranslateTransform.XProperty, frames);
    }
}

/// <summary>
/// Hover / press motion for a tile whose template root (a Grid named "Lift") has RenderTransform = TransformGroup(Scale, Translate)
/// and whose first child is the shadow border. The template's triggers set Hover and Pressed on that Grid.
/// Hover: lift 4px + scale 1.04 + stronger shadow (DurNormal, ease-out). Pressed: scale 0.97 (DurFast).
/// </summary>
public static class TileMotion
{
    /// <summary>True while the tile has keyboard focus reached by keyboard (not by a mouse click). Kept up to date by the tile's focus handlers.</summary>
    public static readonly DependencyProperty KeyFocusedProperty = DependencyProperty.RegisterAttached(
        "KeyFocused", typeof(bool), typeof(TileMotion), new PropertyMetadata(false));

    public static bool GetKeyFocused(DependencyObject d) => (bool)d.GetValue(KeyFocusedProperty);
    public static void SetKeyFocused(DependencyObject d, bool v) => d.SetValue(KeyFocusedProperty, v);

    public static readonly DependencyProperty HoverProperty =DependencyProperty.RegisterAttached(
        "Hover", typeof(bool), typeof(TileMotion), new PropertyMetadata(false, (d, _) => Update(d)));

    public static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached(
        "Pressed", typeof(bool), typeof(TileMotion), new PropertyMetadata(false, (d, _) => Update(d)));

    /// <summary>True while the tile is hovered or has keyboard focus. Inherited, so content inside the tile can react (the "..." button).</summary>
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(TileMotion),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetActive(DependencyObject d) => (bool)d.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject d, bool v) => d.SetValue(ActiveProperty, v);

    public static bool GetHover(DependencyObject d) => (bool)d.GetValue(HoverProperty);
    public static void SetHover(DependencyObject d, bool v) => d.SetValue(HoverProperty, v);
    public static bool GetPressed(DependencyObject d) => (bool)d.GetValue(PressedProperty);
    public static void SetPressed(DependencyObject d, bool v) => d.SetValue(PressedProperty, v);

    /// <summary>
    /// "Subtle" mode for big surfaces (the full reminder card): no hover scale, a 2px lift and a 0.99 press instead of 4px / 1.04 / 0.97.
    /// The Lift grid has the same TransformGroup(Scale, Translate) and shadow-first-child layout as a tile.
    /// </summary>
    public static readonly DependencyProperty SubtleProperty = DependencyProperty.RegisterAttached(
        "Subtle", typeof(bool), typeof(TileMotion), new PropertyMetadata(false));
    public static bool GetSubtle(DependencyObject d) => (bool)d.GetValue(SubtleProperty);
    public static void SetSubtle(DependencyObject d, bool v) => d.SetValue(SubtleProperty, v);

    /// <summary>Scale and vertical offset for a state. Pure, so the numbers are testable.</summary>
    public static (double Scale, double Y) Targets(bool subtle, bool hover, bool pressed)
    {
        if (subtle) return (pressed ? 0.99 : 1.0, hover && !pressed ? -2.0 : 0.0);
        return (pressed ? 0.97 : hover ? 1.04 : 1.0, hover && !pressed ? -4.0 : 0.0);
    }

    /// <summary>
    /// On an outer element whose first child Grid is the Lift grid (TransformGroup of Scale + Translate): pointer enter/leave and a left press
    /// (not on a button inside it) drive that grid's Hover / Pressed. The outer element should be hit-testable over the card's whole
    /// footprint (Background Transparent) so the lift itself never makes the pointer flicker out. Set it false to switch the lift off (compact cards).
    /// </summary>
    public static readonly DependencyProperty SubtleHostProperty = DependencyProperty.RegisterAttached(
        "SubtleHost", typeof(bool), typeof(TileMotion), new PropertyMetadata(false, OnSubtleHostChanged));
    public static bool GetSubtleHost(DependencyObject d) => (bool)d.GetValue(SubtleHostProperty);
    public static void SetSubtleHost(DependencyObject d, bool v) => d.SetValue(SubtleHostProperty, v);

    private static Grid? LiftOf(FrameworkElement host) =>
        (host as Panel)?.Children.OfType<Grid>().FirstOrDefault(g => g.RenderTransform is TransformGroup);

    private static void OnSubtleHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host) return;
        host.MouseEnter -= HostEnter;
        host.MouseLeave -= HostLeave;
        host.PreviewMouseLeftButtonDown -= HostDown;
        host.PreviewMouseLeftButtonUp -= HostUp;
        host.LostMouseCapture -= HostUp;
        if ((bool)e.NewValue)
        {
            host.MouseEnter += HostEnter;
            host.MouseLeave += HostLeave;
            host.PreviewMouseLeftButtonDown += HostDown;
            host.PreviewMouseLeftButtonUp += HostUp;
            host.LostMouseCapture += HostUp;
        }
        else if (LiftOf(host) is { } lift)
        {
            SetPressed(lift, false);
            SetHover(lift, false);
        }
    }

    private static void HostEnter(object sender, RoutedEventArgs e) { if (LiftOf((FrameworkElement)sender) is { } l) SetHover(l, true); }
    private static void HostLeave(object sender, RoutedEventArgs e)
    {
        if (LiftOf((FrameworkElement)sender) is not { } l) return;
        SetPressed(l, false);
        SetHover(l, false);
    }
    private static void HostUp(object sender, RoutedEventArgs e) { if (LiftOf((FrameworkElement)sender) is { } l) SetPressed(l, false); }
    private static void HostDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var host = (FrameworkElement)sender;
        if (LiftOf(host) is not { } l) return;
        // A press on the tick box or a card button is that control's click, not a press of the card itself.
        for (var o = e.OriginalSource as DependencyObject; o is not null && !ReferenceEquals(o, host); o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is System.Windows.Controls.Primitives.ButtonBase) return;
        SetPressed(l, true);
    }

    // Shadow levels come from Styles/Tokens.xaml (Elevation1 = resting, Elevation2 = hover); the fallbacks are the same values.
    private static Thickness ShadowRest => Anim.Lookup("Elevation1Margin") is Thickness t ? t : new(-3, 2, -3, -8);
    private static double OpacityRest => Anim.Lookup("Elevation1Opacity") is double d ? d : 0.55;
    private static Thickness ShadowHover => Anim.Lookup("Elevation2Margin") is Thickness t ? t : new(-7, 6, -7, -16);
    private static double OpacityHover => Anim.Lookup("Elevation2Opacity") is double d ? d : 1.0;

    private static void Update(DependencyObject d)
    {
        if (d is not Grid lift || lift.RenderTransform is not TransformGroup group || group.Children.Count < 2) return;
        // A transform declared in a DataTemplate (the reminder card) is shared and frozen: give this element its own copy.
        if (group.IsFrozen)
        {
            group = group.Clone();
            lift.RenderTransform = group;
        }
        if (group.Children[0] is not ScaleTransform scale || group.Children[1] is not TranslateTransform move) return;

        var pressed = GetPressed(lift);
        var hover = GetHover(lift);
        var (s, y) = Targets(GetSubtle(lift), hover, pressed);
        var dur = Anim.Dur(pressed ? "DurFast" : "DurNormal");
        var shadow = lift.Children.Count > 0 ? lift.Children[0] as Border : null;

        if (!Anim.EffectsEnabled || (dur.HasTimeSpan && dur.TimeSpan == TimeSpan.Zero))
        {
            Set(scale, move, shadow, s, y, hover);
            return;
        }

        void Go(DependencyObject target, DependencyProperty p, double to)
        {
            var a = new DoubleAnimation(to, dur) { EasingFunction = Anim.EaseOut };
            // At rest drop the animation and write the exact value, so the resting transform is exactly identity.
            if (to == (p == TranslateTransform.YProperty ? 0.0 : 1.0))
                a.Completed += (_, _) =>
                {
                    if (GetHover(lift) || GetPressed(lift)) return;
                    ((IAnimatable)target).BeginAnimation(p, null);
                    ((DependencyObject)target).SetValue(p, to);
                };
            ((IAnimatable)target).BeginAnimation(p, a);
        }

        Go(scale, ScaleTransform.ScaleXProperty, s);
        Go(scale, ScaleTransform.ScaleYProperty, s);
        Go(move, TranslateTransform.YProperty, y);

        if (shadow is not null)
        {
            shadow.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(hover ? OpacityHover : OpacityRest, dur) { EasingFunction = Anim.EaseOut });
            shadow.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(hover ? ShadowHover : ShadowRest, dur) { EasingFunction = Anim.EaseOut });
        }
    }

    private static void Set(ScaleTransform scale, TranslateTransform move, Border? shadow, double s, double y, bool hover)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        move.BeginAnimation(TranslateTransform.YProperty, null);
        scale.ScaleX = scale.ScaleY = s;
        move.Y = y;
        if (shadow is not null)
        {
            shadow.BeginAnimation(UIElement.OpacityProperty, null);
            shadow.BeginAnimation(FrameworkElement.MarginProperty, null);
            shadow.Opacity = hover ? OpacityHover : OpacityRest;
            shadow.Margin = hover ? ShadowHover : ShadowRest;
        }
    }
}
