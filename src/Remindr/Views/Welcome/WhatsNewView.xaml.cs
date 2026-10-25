using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Remindr.Services;
using Remindr.Views.Controls;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace Remindr.Views.Welcome;

/// <summary>
/// Full-window What's New overlay. Two uses: the bundled highlights of this release (<see cref="ShowBundled"/>, animated reveal) and the
/// plain release-notes list the update banner opens for a newer, not yet installed version (<see cref="ShowNotes"/>, no card motion).
/// Enter and Esc are "Got it". All motion goes through <see cref="Anim.EffectsEnabled"/>; with the gate off everything shows at once.
/// </summary>
public partial class WhatsNewView : UserControl
{
    private bool _bundled;
    private string? _url;
    private int _reveal;
    private readonly List<(FrameworkElement Host, FrameworkElement Icon)> _cards = new();

    public WhatsNewView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) OnShown();
            else _reveal++;   // cancels any reveal still running
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised once per showing when the user closes it. The argument is true for the bundled page (the version should be remembered).</summary>
    public event EventHandler<bool>? Dismissed;

    /// <summary>The cards of the bundled page, for tests and screenshots.</summary>
    public int CardCount => _cards.Count;

    /// <summary>True while the bundled page (not the release-notes list) is the content.</summary>
    public bool IsBundled => _bundled;

    // ---- content ---------------------------------------------------------------------------------------

    /// <summary>Fills the page with this release's bundled highlights (call before the overlay becomes visible).</summary>
    public void ShowBundled(string version)
    {
        _bundled = true;
        _url = null;
        var title = Loc.Format("WhatsNew_Title", WhatsNewService.DisplayVersion(version));
        SetTitle(title);
        Chip.Visibility = Visibility.Visible;
        ChipText.Text = Loc.Format("Settings_RemindUiVersion", RemindUI.Version);
        GitHubButton.Visibility = Visibility.Collapsed;
        Items.Children.Clear();
        _cards.Clear();
        foreach (var entry in WhatsNewCatalog.For(version)) Items.Children.Add(BuildCard(entry));
        ApplyAccent();
        Resting();
    }

    /// <summary>Fills the page with a newer release's notes as a plain list, with a "View on GitHub" link. No card animation.</summary>
    public void ShowNotes(string version, string body, string url)
    {
        _bundled = false;
        _url = url;
        SetTitle(Loc.Format("WhatsNew_Title", WhatsNewService.DisplayVersion(version)));
        Chip.Visibility = Visibility.Collapsed;
        GitHubButton.Visibility = Visibility.Visible;
        Items.Children.Clear();
        _cards.Clear();
        foreach (var line in ReleaseNotes.Parse(body)) Items.Children.Add(BuildNote(line));
        ApplyAccent();
        Resting();
    }

    private void SetTitle(string title)
    {
        TitleText.Text = title;
        AutomationProperties.SetName(this, title);
    }

    private FrameworkElement BuildNote(NoteLine line)
    {
        switch (line.Kind)
        {
            case NoteLineKind.Heading:
                return new TextBlock { Text = line.Text, Style = (Style)FindResource("Title2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6) };
            case NoteLineKind.Bullet:
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = "•", Style = (Style)FindResource("Body") });
                var text = new TextBlock { Text = line.Text, Style = (Style)FindResource("Body") };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                return row;
            default:
                return new TextBlock { Text = line.Text, Style = (Style)FindResource("Body"), Margin = new Thickness(0, 0, 0, 6) };
        }
    }

    private FrameworkElement BuildCard(WhatsNewEntry entry)
    {
        var title = Loc.Get(entry.TitleKey);
        var text = Loc.Get(entry.TextKey);

        FrameworkElement icon;
        var visual = WhatsNewCatalog.Resolve(entry.Visual);
        if (visual?.PictureValue is { } picture)
            icon = new Image
            {
                Width = 64, Height = 64, Stretch = Stretch.Uniform, FlowDirection = FlowDirection.LeftToRight,
                Source = AvatarCatalog.LoadBuiltin(picture, 128),
            };
        else
        {
            // A symbol sits on a soft accent disc so it weighs about the same as the 3D pictures. The Viewbox fixes the glyph size
            // (a SymbolIcon built in code ignores FontSize until its template applies, and rendered at about 12px).
            var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
            var disc = new Grid { Width = 64, Height = 64 };
            disc.Children.Add(new Border { CornerRadius = new CornerRadius(32), Background = accent, Opacity = 0.14 });
            disc.Children.Add(new Viewbox
            {
                Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new SymbolIcon { Symbol = visual?.Symbol ?? SymbolRegular.Star24, FontSize = 32, Foreground = accent },
            });
            icon = disc;
        }
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var iconHost = new Grid { Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        iconHost.Children.Add(icon);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Title2"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Body"), Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") });

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(iconHost);
        Grid.SetColumn(body, 1);
        content.Children.Add(body);

        // Same shape as ReminderCard: an outer hit-testable host (the reveal moves it), inside it the Lift grid (the subtle hover lift)
        // with the shadow as a sibling behind the card, never an Effect on the card.
        var card = new Border
        {
            Style = (Style)FindResource("GlassCard"), Padding = new Thickness(16), Child = content,
            Focusable = true, FocusVisualStyle = (Style)FindResource("FocusRing"),
        };
        KeyboardNavigation.SetIsTabStop(card, true);
        AutomationProperties.SetName(card, title + ". " + text);
        var lift = new Grid
        {
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } },
        };
        TileMotion.SetSubtle(lift, true);
        lift.Children.Add(new Border { Style = (Style)FindResource("GlassShadow") });
        lift.Children.Add(card);
        var host = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 12) };
        host.Children.Add(lift);
        TileMotion.SetSubtleHost(host, true);
        _cards.Add((host, iconHost));
        return host;
    }

    /// <summary>The blobs take the profile's accent: the accent, a lighter and a darker shade of it.</summary>
    private void ApplyAccent()
    {
        if (TryFindResource("AccentFillColorDefaultBrush") is not SolidColorBrush accent) return;
        var c = accent.Color;
        Color Mix(Color to, double t) => Color.FromRgb(
            (byte)(c.R + (to.R - c.R) * t), (byte)(c.G + (to.G - c.G) * t), (byte)(c.B + (to.B - c.B) * t));
        Blobs.SetColors(new[] { c, Mix(Colors.White, 0.35), Mix(Colors.Black, 0.3), Mix(Colors.White, 0.15) });
    }

    // ---- reveal ----------------------------------------------------------------------------------------

    /// <summary>Everything visible, at rest, Got it default. Also what the gate-off path ends in.</summary>
    private void Resting()
    {
        _reveal++;
        foreach (var element in new UIElement[] { Header, GotItButton }.Concat(_cards.Select(c => (UIElement)c.Host)))
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
        }
        GotItButton.IsDefault = true;
    }

    private void OnShown()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            if (!(_bundled && PlayReveal())) FocusGotIt();
            else Focus();   // Esc and Enter work while the cards are still arriving
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void FocusGotIt()
    {
        GotItButton.IsDefault = true;
        GotItButton.Focus();
    }

    /// <summary>
    /// Header first, then each card (fade, slide up 16, scale 0.96 to 1) StaggerRevealMs apart with its icon popping 120 ms later, then Got it
    /// becomes default and focused. Returns false (and leaves everything at rest) when the gate is off or there is nothing to reveal.
    /// </summary>
    private bool PlayReveal()
    {
        var dur = Anim.Dur("DurEnter");
        if (!Anim.EffectsEnabled || (dur.HasTimeSpan && dur.TimeSpan == TimeSpan.Zero) || _cards.Count == 0) return false;

        var version = ++_reveal;
        var stagger = TimeSpan.FromMilliseconds(Anim.StaggerRevealMs);
        GotItButton.IsDefault = false;   // default only once the reveal ends

        void Fade(UIElement el, TimeSpan begin, Action? done = null)
        {
            el.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = Anim.EaseOut };
            fade.Completed += (_, _) =>
            {
                if (version != _reveal) return;
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = 1;
                done?.Invoke();
            };
            el.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        Fade(Header, TimeSpan.Zero);

        for (var i = 0; i < _cards.Count; i++)
        {
            var (host, icon) = _cards[i];
            var begin = stagger * (i + 1);
            var scale = new ScaleTransform(0.96, 0.96);
            var move = new TranslateTransform(0, 16);
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            host.RenderTransform = new TransformGroup { Children = { scale, move } };
            foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                scale.BeginAnimation(prop, new DoubleAnimation(0.96, 1, dur) { BeginTime = begin, EasingFunction = Anim.EaseOut });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, dur) { BeginTime = begin, EasingFunction = Anim.EaseOut });
            Fade(host, begin, () =>
            {
                host.RenderTransform = Transform.Identity;
            });
            Anim.Pop(icon, begin + TimeSpan.FromMilliseconds(120));
        }

        // Got it comes last.
        var last = stagger * (_cards.Count + 1);
        Fade(GotItButton, last, () => { if (IsVisible) FocusGotIt(); });
        return true;
    }

    // ---- closing ---------------------------------------------------------------------------------------

    private void Close() => Dismissed?.Invoke(this, _bundled);

    private void GotItButton_Click(object sender, RoutedEventArgs e) => Close();

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        if (_url is not null) AppInfo.Open(_url);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && !ReferenceEquals(Keyboard.FocusedElement, GitHubButton))
        {
            // The View on GitHub button handles its own Enter; anywhere else Enter is Got it.
            e.Handled = true;
            Close();
        }
    }
}
