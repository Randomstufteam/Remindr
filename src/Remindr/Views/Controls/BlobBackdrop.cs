using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Remindr.Services;

namespace Remindr.Views.Controls;

/// <summary>
/// Remind UI's drifting colour blobs: four blurred ellipses behind glass (profile picker, welcome greeting, setup).
/// Only the blobs carry a BlurEffect and each is BitmapCache'd, so the blur renders once and the drift just moves the cached bitmap;
/// text is never cached. The drift (20-30 s loops) is paused while the control is hidden or the window is minimised, never starts under
/// reduced motion, and the blobs disappear when Windows transparency is off.
/// <para>
/// Changing colour (<see cref="TransitionTo"/>) cross-fades a second, pre-tinted layer in over the first instead of re-tinting the
/// cached bitmap, so a hue shift costs one blur render and then only opacity changes.
/// </para>
/// </summary>
public sealed class BlobBackdrop : UserControl
{
    private sealed class Blob
    {
        public Grid Host = null!;
        public Ellipse Front = null!;
        public Ellipse? Back;
        public TranslateTransform Move = null!;
        public int Version;
    }

    // size, alignment and margin of each blob (same layout the picker always had).
    private static readonly (double Size, HorizontalAlignment H, VerticalAlignment V, Thickness Margin)[] Layout =
    {
        (520, HorizontalAlignment.Left,  VerticalAlignment.Top,    new Thickness(-140, -160, 0, 0)),
        (440, HorizontalAlignment.Right, VerticalAlignment.Top,    new Thickness(0, -100, -120, 0)),
        (600, HorizontalAlignment.Left,  VerticalAlignment.Bottom, new Thickness(-160, 0, 0, -220)),
        (480, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -100, -160)),
    };

    public static readonly Color[] DefaultColors =
    {
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x10, 0xB9, 0x81)
    };

    private readonly Grid _root = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Blob[] _blobs = new Blob[Layout.Length];
    private readonly List<AnimationClock> _driftClocks = new();
    private bool _driftCreated;
    private Window? _window;

    public BlobBackdrop()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Content = _root;
        for (var i = 0; i < Layout.Length; i++)
        {
            var move = new TranslateTransform();
            var host = new Grid
            {
                Width = Layout[i].Size,
                Height = Layout[i].Size,
                HorizontalAlignment = Layout[i].H,
                VerticalAlignment = Layout[i].V,
                Margin = Layout[i].Margin,
                RenderTransform = move,
            };
            host.SetResourceReference(OpacityProperty, "GlassPickerBlobOpacity");
            var front = NewEllipse(DefaultColors[i]);
            host.Children.Add(front);
            _root.Children.Add(host);
            _blobs[i] = new Blob { Host = host, Front = front, Move = move };
        }

        IsVisibleChanged += (_, _) => UpdateDrift();
        Loaded += (_, _) =>
        {
            if (_window is null && Window.GetWindow(this) is { } w)
            {
                _window = w;
                w.StateChanged += (_, _) => UpdateDrift();
            }
            ShowBlobsIfWanted();
            UpdateDrift();
            ThemeService.GlassChanged -= OnGlassChanged;
            ThemeService.GlassChanged += OnGlassChanged;
        };
        Unloaded += (_, _) => ThemeService.GlassChanged -= OnGlassChanged;
    }

    private static Ellipse NewEllipse(Color color) => new()
    {
        Fill = new SolidColorBrush(color),
        // The blur is baked into the cache: later moves and fades never re-run it.
        Effect = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = 80,
            KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        },
        CacheMode = new BitmapCache(),
    };

    // ---- colour ------------------------------------------------------------------------------------

    /// <summary>Sets the four blob colours at once (extra colours are ignored, missing ones repeat from the start).</summary>
    public void SetColors(IReadOnlyList<Color> colors)
    {
        if (colors.Count == 0) return;
        for (var i = 0; i < _blobs.Length; i++)
        {
            var b = _blobs[i];
            b.Version++;
            if (b.Back is not null)
            {
                b.Back.BeginAnimation(OpacityProperty, null);
                b.Front.BeginAnimation(OpacityProperty, null);
                b.Back.Opacity = 0;
                b.Front.Opacity = 1;
            }
            b.Front.Fill = new SolidColorBrush(colors[i % colors.Count]);
        }
    }

    /// <summary>Cross-fades the blobs to new colours. With motion off (or a zero duration) it is the same as <see cref="SetColors"/>.</summary>
    public void TransitionTo(IReadOnlyList<Color> colors, TimeSpan duration)
    {
        if (colors.Count == 0) return;
        if (!Anim.MotionEnabled || duration <= TimeSpan.Zero || !IsLoaded)
        {
            SetColors(colors);
            return;
        }
        for (var i = 0; i < _blobs.Length; i++)
        {
            var b = _blobs[i];
            var version = ++b.Version;
            if (b.Back is null)
            {
                b.Back = NewEllipse(colors[i % colors.Count]);
                b.Back.Opacity = 0;
                b.Host.Children.Add(b.Back);
            }
            else
            {
                b.Back.Fill = new SolidColorBrush(colors[i % colors.Count]);
            }

            // From wherever the previous fade got to, so a quick succession of changes stays smooth.
            var incoming = b.Back;
            var outgoing = b.Front;
            var fadeIn = new DoubleAnimation(1, duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            fadeIn.Completed += (_, _) =>
            {
                if (version != b.Version) return;
                // The incoming layer is now the front one; the old front becomes the spare for the next change.
                incoming.BeginAnimation(OpacityProperty, null);
                outgoing.BeginAnimation(OpacityProperty, null);
                incoming.Opacity = 1;
                outgoing.Opacity = 0;
                b.Front = incoming;
                b.Back = outgoing;
            };
            incoming.BeginAnimation(OpacityProperty, fadeIn);
            outgoing.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    // ---- drift -------------------------------------------------------------------------------------

    private bool BlobsWanted => TryFindResource("GlassPickerBlobOpacity") is double d && d > 0;

    private void ShowBlobsIfWanted() => _root.Visibility = BlobsWanted ? Visibility.Visible : Visibility.Collapsed;

    private void OnGlassChanged(object? sender, EventArgs e)
    {
        // UpdateGlass may run from the SystemEvents dispatch; the dictionary swap is done by now.
        Dispatcher.BeginInvoke(new Action(() => { ShowBlobsIfWanted(); UpdateDrift(); }));
    }

    /// <summary>Slow looping drift (20-30 s per loop, AutoReverse). Paused when hidden or minimised; never created under reduced motion.</summary>
    private void UpdateDrift()
    {
        var run = IsVisible && IsLoaded && BlobsWanted && Anim.MotionEnabled && _window?.WindowState != WindowState.Minimized;
        if (run && !_driftCreated) CreateDrift();
        foreach (var clock in _driftClocks)
        {
            if (run) clock.Controller?.Resume();
            else clock.Controller?.Pause();
        }
    }

    private void CreateDrift()
    {
        _driftCreated = true;
        // x amplitude, y amplitude, x seconds, y seconds: different per blob so they never line up.
        (double ax, double ay, double tx, double ty)[] plan =
        {
            (110, 80, 22, 27), (-120, 90, 26, 21), (100, -70, 29, 24), (-90, -100, 24, 30)
        };
        for (var i = 0; i < _blobs.Length; i++)
        {
            var transform = _blobs[i].Move;
            var p = plan[i];
            foreach (var (prop, to, secs) in new[] { (TranslateTransform.XProperty, p.ax, p.tx), (TranslateTransform.YProperty, p.ay, p.ty) })
            {
                var anim = new DoubleAnimation(0, to, TimeSpan.FromSeconds(secs))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                // A drift this slow looks identical at 30 fps and halves the per-frame recomposition (GPU idle target).
                Timeline.SetDesiredFrameRate(anim, 30);
                var clock = anim.CreateClock();
                transform.ApplyAnimationClock(prop, clock);
                _driftClocks.Add(clock);
            }
        }
    }
}
