using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Remindr.Services;
using Remindr.Services.Assistant;

namespace Remindr.Views.Controls;

/// <summary>
/// The voice orb: a glass disc with three blurred blobs in a circular clip, in the profile accent. Driven by <see cref="State"/> and a plain
/// <see cref="Level"/> (0..1, already smoothed), so it works with or without audio. Idle drifts slowly (about 6 s), Listening and Speaking
/// swell with the level, Thinking turns slowly, Muted is gray. Only transforms move (the blur is cached once per colour). With
/// <see cref="Anim.EffectsEnabled"/> off the blobs are gone and it is a static ring that changes colour per state. The whole orb is the mic button
/// (<see cref="Command"/>), and the state name is announced politely.
/// </summary>
public partial class VoiceOrb : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(OrbState), typeof(VoiceOrb),
        new PropertyMetadata(OrbState.Idle, (d, _) => ((VoiceOrb)d).OnStateChanged()));
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(VoiceOrb),
        new PropertyMetadata(0.0, (d, _) => ((VoiceOrb)d).ApplyLevel()));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(VoiceOrb));

    public OrbState State { get => (OrbState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// <summary>0..1; only Listening and Speaking follow it.</summary>
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private sealed class Blob
    {
        public Ellipse Shape = null!;
        public TranslateTransform Move = null!;
        public ScaleTransform Scale = null!;
        public double Swell;
    }

    private static readonly Color[] StateTint =
    {
        Colors.Transparent,                        // Idle: the accent
        Color.FromRgb(0x10, 0xB9, 0x81),           // Listening
        Color.FromRgb(0xF5, 0x9E, 0x0B),           // Thinking
        Color.FromRgb(0x8B, 0x5C, 0xF6),           // Speaking
        Color.FromRgb(0x8A, 0x8A, 0x8A),           // Muted
    };

    private readonly Blob[] _blobs = new Blob[3];
    private readonly ScaleTransform _hostScale = new(1, 1);
    private readonly RotateTransform _hostTurn = new();
    private readonly List<AnimationClock> _drift = new();
    private AnimationClock? _turn;
    private bool _driftCreated;
    private Window? _window;

    public VoiceOrb()
    {
        InitializeComponent();
        Disc.Clip = new EllipseGeometry(new Point(30, 30), 30, 30);
        var group = new TransformGroup();
        group.Children.Add(_hostScale);
        group.Children.Add(_hostTurn);
        BlobHost.RenderTransform = group;

        // size, x offset, y offset, swell factor
        var layout = new (double Size, double X, double Y, double Swell)[] { (38, -8, -6, 0.55), (32, 10, 4, 0.40), (28, -2, 12, 0.70) };
        for (var i = 0; i < _blobs.Length; i++)
        {
            var move = new TranslateTransform(layout[i].X, layout[i].Y);
            var scale = new ScaleTransform(1, 1);
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(move);
            var shape = new Ellipse
            {
                Width = layout[i].Size,
                Height = layout[i].Size,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = tg,
                Effect = new BlurEffect { Radius = 10, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
                CacheMode = new BitmapCache(),
            };
            BlobHost.Children.Add(shape);
            _blobs[i] = new Blob { Shape = shape, Move = move, Scale = scale, Swell = layout[i].Swell };
        }

        Hit.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(nameof(Command)) { Source = this });
        IsVisibleChanged += (_, _) => UpdateMotion();
        Loaded += (_, _) =>
        {
            if (_window is null && Window.GetWindow(this) is { } w)
            {
                _window = w;
                w.StateChanged += (_, _) => UpdateMotion();
            }
            Apply(announce: false);
        };
        Loc.CultureChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => Apply(announce: false)));
        // "Reduce visual effects" switched live: become the static ring (or the blobs again) without waiting for a state change.
        Anim.EffectsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { Apply(announce: false); ApplyLevel(); }));
        Apply(announce: false);
    }

    private void OnStateChanged()
    {
        Apply(announce: IsLoaded);
        ApplyLevel();
    }

    /// <summary>Colours, ring, name and motion for the current state and effects gate.</summary>
    private void Apply(bool announce)
    {
        var state = State;
        var moves = OrbModel.Moves(state, Anim.EffectsEnabled);
        var accent = TryFindResource("AccentFillColorDefaultBrush") is SolidColorBrush b ? b.Color : Color.FromRgb(0x3B, 0x82, 0xF6);
        var tint = state == OrbState.Idle ? accent : StateTint[(int)state];

        Ring.Stroke = new SolidColorBrush(tint);
        // The static ring (effects off) is bolder and tints the disc faintly, so the state is still readable without motion.
        Ring.StrokeThickness = moves ? 1.5 : 4;
        Glass.Opacity = 1;
        BlobHost.Visibility = Anim.EffectsEnabled ? Visibility.Visible : Visibility.Collapsed;
        BlobHost.Opacity = state == OrbState.Muted ? 0.45 : 1;
        if (!Anim.EffectsEnabled) Glass.Fill = new SolidColorBrush(Color.FromArgb(0x38, tint.R, tint.G, tint.B));
        else Glass.SetResourceReference(Shape.FillProperty, "GlassChipBrush");

        _blobs[0].Shape.Fill = new SolidColorBrush(tint);
        _blobs[1].Shape.Fill = new SolidColorBrush(Mix(tint, Colors.White, 0.4));
        _blobs[2].Shape.Fill = new SolidColorBrush(Mix(tint, accent, 0.5));

        var name = Loc.Get(OrbModel.NameKey(state));
        System.Windows.Automation.AutomationProperties.SetName(Hit, name);
        System.Windows.Automation.AutomationProperties.SetHelpText(Hit, name);
        if (announce && OrbModel.Announces(state) && UIElementAutomationPeer.FromElement(Hit) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        UpdateMotion();
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Level to scale: the whole group swells a little and each blob a different amount. Plain property writes, no animation.</summary>
    private void ApplyLevel()
    {
        var level = OrbModel.EffectiveLevel(State, Level);
        if (!OrbModel.Moves(State, Anim.EffectsEnabled)) level = 0;
        _hostScale.ScaleX = _hostScale.ScaleY = 1 + 0.18 * level;
        foreach (var b in _blobs) b.Scale.ScaleX = b.Scale.ScaleY = 1 + b.Swell * level;
    }

    private void UpdateMotion()
    {
        var run = OrbModel.Moves(State, Anim.EffectsEnabled) && IsVisible && IsLoaded && _window?.WindowState != WindowState.Minimized;
        if (run && !_driftCreated) CreateClocks();
        foreach (var c in _drift)
        {
            if (run) c.Controller?.Resume();
            else c.Controller?.Pause();
        }
        if (_turn is not null)
        {
            if (run && State == OrbState.Thinking) _turn.Controller?.Resume();
            else { _turn.Controller?.Pause(); if (State != OrbState.Thinking) _hostTurn.Angle = 0; }
        }
    }

    private void CreateClocks()
    {
        _driftCreated = true;
        // Different periods around 6 s so the blobs never line up.
        var plan = new (double Ax, double Ay, double Tx, double Ty)[] { (7, 5, 6.0, 7.3), (-6, 7, 6.8, 5.6), (5, -7, 5.7, 6.5) };
        for (var i = 0; i < _blobs.Length; i++)
        {
            var p = plan[i];
            var move = _blobs[i].Move;
            foreach (var (prop, delta, secs) in new[] { (TranslateTransform.XProperty, p.Ax, p.Tx), (TranslateTransform.YProperty, p.Ay, p.Ty) })
            {
                var from = (double)move.GetValue(prop);
                var anim = new DoubleAnimation(from, from + delta, TimeSpan.FromSeconds(secs))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, 30);
                var clock = anim.CreateClock();
                move.ApplyAnimationClock(prop, clock);
                _drift.Add(clock);
            }
        }
        var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(turn, 30);
        _turn = turn.CreateClock();
        _hostTurn.ApplyAnimationClock(RotateTransform.AngleProperty, _turn);
    }
}
