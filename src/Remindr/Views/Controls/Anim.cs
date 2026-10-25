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
    public static void Pop(UIElement el)
    {
        var dur = Dur("DurEnter");
        if (!EffectsEnabled || IsZero(dur)) return;

        var version = (int)el.GetValue(VersionProperty) + 1;
        el.SetValue(VersionProperty, version);

        var scale = new ScaleTransform(0.85, 0.85);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        var a = new DoubleAnimation(0.85, 1, dur) { EasingFunction = EasePop };
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

    // Shadow levels come from Styles/Tokens.xaml (Elevation1 = resting, Elevation2 = hover); the fallbacks are the same values.
    private static Thickness ShadowRest => Anim.Lookup("Elevation1Margin") is Thickness t ? t : new(-3, 2, -3, -8);
    private static double OpacityRest => Anim.Lookup("Elevation1Opacity") is double d ? d : 0.55;
    private static Thickness ShadowHover => Anim.Lookup("Elevation2Margin") is Thickness t ? t : new(-7, 6, -7, -16);
    private static double OpacityHover => Anim.Lookup("Elevation2Opacity") is double d ? d : 1.0;

    private static void Update(DependencyObject d)
    {
        if (d is not Grid lift || lift.RenderTransform is not TransformGroup group || group.Children.Count < 2) return;
        if (group.Children[0] is not ScaleTransform scale || group.Children[1] is not TranslateTransform move) return;

        var pressed = GetPressed(lift);
        var hover = GetHover(lift);
        var s = pressed ? 0.97 : hover ? 1.04 : 1.0;
        var y = hover && !pressed ? -4.0 : 0.0;
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
