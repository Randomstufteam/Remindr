using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Remindr.Services;
using Remindr.Views.Controls;

namespace Remindr.Views.Welcome;

/// <summary>
/// The welcome greeting: one large word at a time, each in its own language and script, on drifting colour blobs.
/// Motion per word (Remind UI's own, not a stroke-drawn script): opacity 0 to 1, blur 14 to 0, scale 0.94 to 1 and a slight
/// horizontal "settle" (ScaleX 1.06 to 1), while the previous word blurs out and the blob hue shifts. About 2.25 s per word (GreetingCatalog.WordPeriod).
/// With reduced motion the words just swap every 1.6 s and the blobs stay still. Everything pauses while hidden or minimised.
/// Any key or click (or the Get started button) raises <see cref="Continued"/> once.
/// </summary>
public partial class WelcomeView : UserControl
{
    private const string BaseFont = "Segoe UI Variable Display";

    private readonly DispatcherTimer _timer = new() { Interval = GreetingCatalog.WordPeriod };
    private readonly Dictionary<TextBlock, int> _versions = new();
    private IReadOnlyList<Greeting> _words = Array.Empty<Greeting>();
    private int _index;
    private bool _nextIsB;
    private bool _continued;
    private Window? _window;

    public WelcomeView()
    {
        InitializeComponent();
        foreach (var slot in new[] { WordA, WordB })
        {
            slot.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new ScaleTransform(1, 1), new TranslateTransform() } };
            _versions[slot] = 0;
        }

        _timer.Tick += (_, _) => AdvanceWord();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) BeginSession();
            else _timer.Stop();
        };
        Loaded += (_, _) =>
        {
            if (_window is null && Window.GetWindow(this) is { } w)
            {
                _window = w;
                w.StateChanged += (_, _) => UpdateRunning();
            }
            // Visible before it was loaded (first run shows it at start-up): begin now that the blobs can animate.
            if (IsVisible && _words.Count == 0) BeginSession();
            else UpdateRunning();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Raised once per showing when the user presses a key, clicks, or uses Get started.</summary>
    public event EventHandler? Continued;

    /// <summary>True when shown over the running app: adds an almost opaque backing so the app underneath does not show through.</summary>
    public bool UseBacking
    {
        get => Backing.Visibility == Visibility.Visible;
        set => Backing.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The word currently shown (for tests and screenshots).</summary>
    public string CurrentWord => _words.Count == 0 ? "" : _words[_index % _words.Count].Word;

    // ---- session ---------------------------------------------------------------------------------------

    private void BeginSession()
    {
        _continued = false;
        _timer.Stop();
        _words = GreetingCatalog.OrderedFor(Loc.ResourceCulture.Name);
        _index = 0;
        _nextIsB = false;
        foreach (var slot in new[] { WordA, WordB })
        {
            Reset(slot);
            slot.Text = "";
            slot.Opacity = 0;
        }
        ShowWord(_words[0]);
        UpdateRunning();

        // Default-button focus so Enter works at once (and the focus ring shows for keyboard users).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            StartButton.Focus();
            Keyboard.Focus(StartButton);
        }), DispatcherPriority.Input);
    }

    private void UpdateRunning()
    {
        var run = IsVisible && IsLoaded && _words.Count > 0 && _window?.WindowState != WindowState.Minimized;
        if (run) _timer.Start();
        else _timer.Stop();
    }

    private void AdvanceWord()
    {
        if (_words.Count == 0) return;
        _index = (_index + 1) % _words.Count;
        ShowWord(_words[_index]);
    }

    // ---- one word --------------------------------------------------------------------------------------

    private void ShowWord(Greeting g)
    {
        var slot = _nextIsB ? WordB : WordA;
        var previous = _nextIsB ? WordA : WordB;
        _nextIsB = !_nextIsB;

        slot.Text = g.Word;
        // Each word is shaped, directed and fonted by its own language (Arabic joins and runs right to left, Thai/Hindi get their fonts).
        slot.Language = XmlLanguage.GetLanguage(g.Culture);
        slot.FlowDirection = g.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        slot.FontFamily = new FontFamily(FontPolicy.Chain(BaseFont, g.Code));
        slot.FontWeight = FontWeight.FromOpenTypeWeight(350); // SemiLight
        Panel.SetZIndex(slot, 1);
        Panel.SetZIndex(previous, 0);

        var colors = GreetingCatalog.BlobColors(HueIndex(g)).Select(c => Color.FromRgb(c.R, c.G, c.B)).ToList();
        // The hue tween runs over the same window as the incoming word, so colour and text move together.
        Blobs.TransitionTo(colors, InDuration);

        if (!Anim.EffectsEnabled)
        {
            CrossFade(slot, previous);
            return;
        }
        AnimateIn(slot);
        AnimateOut(previous);
    }

    private int HueIndex(Greeting g)
    {
        var all = GreetingCatalog.All;
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], g)) return i;
        return 0;
    }

    private static (ScaleTransform Uniform, ScaleTransform Settle, TranslateTransform Move) Transforms(TextBlock t)
    {
        var group = (TransformGroup)t.RenderTransform;
        return ((ScaleTransform)group.Children[0], (ScaleTransform)group.Children[1], (TranslateTransform)group.Children[2]);
    }

    /// <summary>Stops every animation on the slot and puts back its exact resting transform (no effect, scale 1, no offset).</summary>
    private void Reset(TextBlock t)
    {
        _versions[t]++;
        var (uniform, settle, move) = Transforms(t);
        t.BeginAnimation(OpacityProperty, null);
        foreach (var s in new[] { uniform, settle })
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            s.ScaleX = s.ScaleY = 1;
        }
        move.BeginAnimation(TranslateTransform.YProperty, null);
        move.Y = 0;
        t.Effect = null;
    }

    // Timings (the words overlap: the outgoing one is still fading while the incoming one is already arriving).
    private static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan InDelay = TimeSpan.FromMilliseconds(120);

    private static Duration D(TimeSpan t) => new(t);

    /// <summary>Reduced motion: the words simply swap (no fade, no blur, no movement), as the plan specifies.</summary>
    private void CrossFade(TextBlock incoming, TextBlock outgoing)
    {
        Reset(incoming);
        Reset(outgoing);
        incoming.Opacity = 1;
        outgoing.Opacity = 0;
    }

    private void AnimateIn(TextBlock t)
    {
        Reset(t);
        var version = _versions[t];
        var dur = D(InDuration);
        var (uniform, settle, move) = Transforms(t);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        var blur = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Performance };
        t.Effect = blur;
        t.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, dur) { BeginTime = InDelay, EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (_versions[t] != version) return;
            t.BeginAnimation(OpacityProperty, null);
            t.Opacity = 1;
        };
        t.BeginAnimation(OpacityProperty, fade);

        // The text is sharp again once the blur has gone (an Effect would otherwise keep the text rasterised).
        var unblur = new DoubleAnimation(14, 0, dur) { BeginTime = InDelay, EasingFunction = ease };
        unblur.Completed += (_, _) =>
        {
            if (_versions[t] == version && ReferenceEquals(t.Effect, blur)) t.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);

        move.Y = 18;
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, dur) { BeginTime = InDelay, EasingFunction = ease });
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            uniform.BeginAnimation(prop, new DoubleAnimation(1.04, 1, dur) { BeginTime = InDelay, EasingFunction = ease });
        // Tracking settle: starts a touch wide and tightens to its natural width.
        settle.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.03, 1, dur) { BeginTime = InDelay, EasingFunction = ease });
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            uniform.SetValue(prop, 1.04); // value before the delayed animation begins
    }

    private void AnimateOut(TextBlock t)
    {
        if (t.Opacity <= 0.001) return;
        var from = t.Opacity;
        Reset(t);
        var version = _versions[t];
        var dur = D(OutDuration);
        var (uniform, _, move) = Transforms(t);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
        t.Effect = blur;
        t.Opacity = from;

        var fade = new DoubleAnimation(from, 0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (_versions[t] != version) return;
            t.BeginAnimation(OpacityProperty, null);
            t.Opacity = 0;
            t.Effect = null;
        };
        t.BeginAnimation(OpacityProperty, fade);
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 14, dur) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -18, dur) { EasingFunction = ease });
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            uniform.BeginAnimation(prop, new DoubleAnimation(1, 0.96, dur) { EasingFunction = ease });
    }
    // ---- input -----------------------------------------------------------------------------------------

    /// <summary>Continues (once per showing). Safe to call from outside, e.g. the window's key fallback.</summary>
    public void Dismiss()
    {
        if (_continued) return;
        _continued = true;
        _timer.Stop();
        Continued?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        TryDismissFromKey(e);
    }

    /// <summary>Continues on any real key (not a lone modifier, not Alt+F4). Also used by the window when keyboard focus is not inside the greeting.</summary>
    public bool TryDismissFromKey(KeyEventArgs e)
    {
        if (IsModifierOnly(e)) return false;
        e.Handled = true;
        Dismiss();
        return true;
    }

    /// <summary>Alt+F4 must still close the window, and a lone modifier key is not "a key".</summary>
    private static bool IsModifierOnly(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.Key == Key.System && key == Key.F4) return true;
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock or Key.Scroll or Key.None or Key.ImeProcessed;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Dismiss();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => Dismiss();
}
