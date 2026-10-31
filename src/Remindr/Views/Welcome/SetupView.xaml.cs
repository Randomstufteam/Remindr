using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Remindr.Converters;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Controls;

namespace Remindr.Views.Welcome;

/// <summary>
/// First-run setup: five pages on one glass card. All the state and rules live in <see cref="SetupViewModel"/>; this class shows the page for
/// the current step (slide + fade, in the reading direction, so it mirrors under right-to-left), moves keyboard focus to the first field of
/// each page, and keeps the live bits in sync (blob colour follows the profile colour, the summary follows the choices).
/// Enter = Next (or Start on the last page), Esc = Back.
/// </summary>
public partial class SetupView : UserControl
{
    private const double SlideDistance = 28;

    private readonly FrameworkElement[] _pages;
    private readonly Dictionary<FrameworkElement, int> _versions = new();
    private AvatarChooserViewModel _chooser = new(null, ProfileEditViewModel.Palette[0], "?");
    private readonly IReadOnlyList<ThemeOption> _themes = new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System }.Select(t => new ThemeOption(t)).ToList();
    private SetupViewModel? _vm;
    private int _shownIndex;
    private bool _syncingTheme, _syncingPin, _finishing;

    public SetupView()
    {
        InitializeComponent();
        _pages = new FrameworkElement[] { PageLanguage, PageAppearance, PageProfile, PagePin, PageDone };
        foreach (var page in _pages)
        {
            page.RenderTransform = new TranslateTransform();
            _versions[page] = 0;
        }

        ColorList.ItemsSource = ProfileEditViewModel.Palette;
        ThemeList.ItemsSource = _themes;
        BindChooser(_chooser);
        PinPanel.StateChanged += (_, _) =>
        {
            if (_vm is null || _syncingPin) return;
            _vm.Pin = PinPanel.NewPin;
            _vm.PinConfirm = PinPanel.RepeatPin;
        };

        Root.SizeChanged += (_, _) => FitCard();
        Loc.CultureChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            // Only while setup is in progress: afterwards (or when it never ran) Settings language switches must not rebuild a hidden chooser.
            if (_vm is null || _vm.Created is not null) return;
            RefreshSummary();
            RebuildChooser();
        }));
    }

    private void BindChooser(AvatarChooserViewModel chooser)
    {
        _chooser.PropertyChanged -= OnChooserChanged;
        _chooser = chooser;
        Picker.DataContext = chooser.Picker;
        chooser.PropertyChanged += OnChooserChanged;
    }

    private void OnChooserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AvatarChooserViewModel.Avatar) && _vm is not null) _vm.Avatar = _chooser.Avatar;
    }

    /// <summary>
    /// The picture chips ("All", "People"...) are text built when the chooser is created, so after a language switch the chooser is made
    /// again (keeping the chosen picture) to show them in the new language.
    /// </summary>
    private void RebuildChooser()
    {
        Picker.ResetSearch();
        BindChooser(new AvatarChooserViewModel(_vm?.Avatar, _vm?.Color ?? ProfileEditViewModel.Palette[0], _vm?.Initial ?? "?"));
    }

    /// <summary>Raised when setup is complete: the created profile, and whether the person asked for their first reminder.</summary>
    public event Action<Profile, bool>? Finished;

    /// <summary>A profile already existed when setup tried to create the first one; the host should show the profile picker.</summary>
    public event Action? AlreadySetUp;

    /// <summary>The page shown now (0 to 4); for tests and screenshots.</summary>
    public int ShownIndex => _shownIndex;

    // ---- wiring -----------------------------------------------------------------------------------------

    public void Attach(SetupViewModel vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;

        _shownIndex = vm.StepIndex;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == _shownIndex ? Visibility.Visible : Visibility.Collapsed;
            ResetPage(_pages[i]);
        }
        ApplyFooter(vm.StepIndex);
        DotsList.SelectedIndex = vm.StepIndex;
        SelectCurrentLanguage();
        SelectCurrentTheme();
        Blobs.SetColors(BlobColorsFor(vm.Color));
        RefreshSummary();
        FitCard();
    }

    private void FitCard()
    {
        if (Root.ActualHeight <= 0) return;
        // 520 tall by default; on a short window it shrinks (the Profile page scrolls) and never leaves the screen.
        CardHost.Height = Math.Clamp(Root.ActualHeight - 56, 360, 520);
        CardHost.Width = Math.Clamp(Root.ActualWidth - 48, 320, 560);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        switch (e.PropertyName)
        {
            case nameof(SetupViewModel.Step):
                var from = _shownIndex;
                _shownIndex = _vm.StepIndex;
                DotsList.SelectedIndex = _vm.StepIndex;
                ShowStep(from, _shownIndex);
                break;
            case nameof(SetupViewModel.LanguageCode):
                LanguageControl.SelectedCode = _vm.LanguageCode;
                RefreshSummary();
                break;
            case nameof(SetupViewModel.Theme):
                SelectCurrentTheme();
                RefreshSummary();
                break;
            case nameof(SetupViewModel.Color):
                Blobs.TransitionTo(BlobColorsFor(_vm.Color), Anim.Dur("DurHue").TimeSpan);
                break;
            case nameof(SetupViewModel.Avatar):
                // The vm is the source of truth (e.g. when it is reset); keep the chooser's highlight in line.
                if (_chooser.Avatar != _vm.Avatar && !_chooser.HasUpload) { if (_vm.Avatar is null) _chooser.UseInitialOnly(); }
                break;
            case nameof(SetupViewModel.Name):
            case nameof(SetupViewModel.HasPin):
                RefreshSummary();
                break;
            case nameof(SetupViewModel.Pin):
            case nameof(SetupViewModel.PinConfirm):
                // Skip empties both; reflect that in the boxes.
                _syncingPin = true;
                PinPanel.SetValues(_vm.Pin, _vm.PinConfirm);
                _syncingPin = false;
                break;
        }
    }

    private static IReadOnlyList<Color> BlobColorsFor(string hex)
    {
        var c = ColorUtil.Parse(hex);
        static byte Mix(byte v) => (byte)(v + (255 - v) * 0.35);
        var tint = Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        var cool = BlobBackdrop.DefaultColors[1];
        return new[] { c, tint, c, cool };
    }

    // ---- language & theme lists ------------------------------------------------------------------------

    private void SelectCurrentLanguage()
    {
        if (_vm is null) return;
        LanguageControl.PinnedCode = _vm.SuggestedLanguageCode;
        LanguageControl.SelectedCode = _vm.LanguageCode;
    }

    /// <summary>A language was chosen in the picker: the app previews it live (the choice is saved when setup is finished).</summary>
    private void LanguageControl_LanguageChosen(string code)
    {
        if (_vm is not null) _vm.LanguageCode = code;
    }

    private void SelectCurrentTheme()
    {
        if (_vm is null) return;
        _syncingTheme = true;
        ThemeList.SelectedItem = _themes.First(t => t.Theme == _vm.Theme);
        _syncingTheme = false;
    }

    private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTheme || _vm is null) return;
        if (ThemeList.SelectedItem is ThemeOption option) _vm.Theme = option.Theme;
    }

    // ---- profile ---------------------------------------------------------------------------------------

    private void InitialOnly_Click(object sender, RoutedEventArgs e) => _chooser.UseInitialOnly();

    // ---- buttons ---------------------------------------------------------------------------------------

    private void Next_Click(object sender, RoutedEventArgs e) => _vm?.Next();
    private void Back_Click(object sender, RoutedEventArgs e) => _vm?.Back();
    private void Skip_Click(object sender, RoutedEventArgs e) => _vm?.Skip();
    private void Start_Click(object sender, RoutedEventArgs e) => Finish(false);
    private void AddFirst_Click(object sender, RoutedEventArgs e) => Finish(true);

    private void Finish(bool addFirstReminder)
    {
        if (_vm is null || _finishing) return;
        _finishing = true;
        try
        {
            var profile = _vm.Complete();
            Finished?.Invoke(profile, addFirstReminder);
        }
        catch (Data.Repositories.FirstRunAlreadyCompletedException)
        {
            // Someone else finished setup already: no new profile, just go on to the picker.
            AlreadySetUp?.Invoke();
        }
        catch
        {
            _finishing = false;
            throw; // reaches the app's unhandled-exception message and error.log
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_vm is null || e.Handled) return;
        // On the Language page the picker gets Enter (choose the highlighted language, a second Enter goes on) and Esc (clear the search) first.
        if (_vm.Step == SetupStep.Language && LanguageControl.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter && LanguageControl.ActivateCandidate()) { e.Handled = true; return; }
            if (e.Key == Key.Escape && LanguageControl.ClearSearch()) { e.Handled = true; return; }
        }

        if (e.Key == Key.Escape)
        {
            if (_vm.CanBack) { _vm.Back(); e.Handled = true; }
            return;
        }
        if (e.Key != Key.Enter) return;

        // A focused button does its own thing on Enter (Back, Skip, Next, Start, a picture...).
        if (Keyboard.FocusedElement is Button) return;
        if (_vm.IsDone)
        {
            Finish(false);
            e.Handled = true;
        }
        else if (_vm.CanNext)
        {
            _vm.Next();
            e.Handled = true;
        }
    }

    // ---- showing pages ---------------------------------------------------------------------------------

    private void ApplyFooter(int index)
    {
        var done = index == (int)SetupStep.Done;
        FooterSteps.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        FooterDone.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetPage(FrameworkElement page)
    {
        _versions[page]++;
        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 1;
        page.IsHitTestVisible = true;
        if (page.RenderTransform is TranslateTransform t)
        {
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.X = 0;
        }
    }

    /// <summary>
    /// Slides the old page out and the new one in (forward: the new page comes from the trailing side; Back: from the leading side).
    /// Under right-to-left the layout is mirrored, so the same X offsets read the other way round on screen.
    /// </summary>
    private void ShowStep(int from, int to)
    {
        if (from == to) return;
        var dir = to > from ? 1 : -1;
        var outgoing = _pages[from];
        var incoming = _pages[to];
        ApplyFooter(to);
        foreach (var p in _pages)
            if (!ReferenceEquals(p, outgoing) && !ReferenceEquals(p, incoming)) { p.Visibility = Visibility.Collapsed; ResetPage(p); }

        var outDur = Anim.Dur("DurNormal");
        var inDur = Anim.Dur("DurSlow");
        var animate = IsLoaded && Anim.MotionEnabled && outDur.HasTimeSpan && outDur.TimeSpan > TimeSpan.Zero;

        ResetPage(outgoing);
        ResetPage(incoming);
        incoming.Visibility = Visibility.Visible;

        if (!animate)
        {
            outgoing.Visibility = Visibility.Collapsed;
            AfterStep(to);
            return;
        }

        outgoing.IsHitTestVisible = false;
        var outVersion = _versions[outgoing];
        var outMove = (TranslateTransform)outgoing.RenderTransform;
        outMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, -dir * SlideDistance, outDur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fadeOut = new DoubleAnimation(1, 0, outDur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) =>
        {
            if (_versions[outgoing] != outVersion) return;
            outgoing.Visibility = Visibility.Collapsed;
            ResetPage(outgoing);
        };
        outgoing.BeginAnimation(OpacityProperty, fadeOut);

        var inVersion = _versions[incoming];
        var inMove = (TranslateTransform)incoming.RenderTransform;
        incoming.Opacity = 0;
        inMove.X = dir * SlideDistance;
        var delay = TimeSpan.FromMilliseconds(Anim.StaggerMs);
        inMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(dir * SlideDistance, 0, inDur) { BeginTime = delay, EasingFunction = Anim.EaseOut });
        var fadeIn = new DoubleAnimation(0, 1, inDur) { BeginTime = delay, EasingFunction = Anim.EaseOut };
        fadeIn.Completed += (_, _) =>
        {
            if (_versions[incoming] != inVersion) return;
            ResetPage(incoming);
        };
        incoming.BeginAnimation(OpacityProperty, fadeIn);

        AfterStep(to);
    }

    /// <summary>Focus goes to the first field of the page that just appeared; the last page also plays its little celebration.</summary>
    private void AfterStep(int index)
    {
        Dispatcher.BeginInvoke(new Action(() => FocusPage(index)), System.Windows.Threading.DispatcherPriority.Input);
        if (index == (int)SetupStep.Done) PlayDone();
    }

    /// <summary>Puts keyboard focus on the first control of a page (search box, chosen theme, name, new PIN, Start).</summary>
    public void FocusPage(int index)
    {
        if (!IsVisible) return;
        switch (index)
        {
            case 0:
                LanguageControl.FocusSearch();
                break;
            case 1:
                if (ThemeList.SelectedItem is { } theme && ThemeList.ItemContainerGenerator.ContainerFromItem(theme) is ListBoxItem card) card.Focus();
                else ThemeList.Focus();
                break;
            case 2:
                NameBox.Focus();
                break;
            case 3:
                PinPanel.FocusFirst();
                break;
            default:
                StartButton.Focus();
                break;
        }
    }

    // ---- summary page ----------------------------------------------------------------------------------

    private void RefreshSummary()
    {
        if (_vm is null) return;
        DoneTitle.Text = Loc.Format("Setup_Done_Title", _vm.DisplayName);
        SumProfile.Text = _vm.DisplayName;
        SumLanguage.Text = _vm.LanguageNativeName;
        SumTheme.Text = Loc.Get("Settings_Theme_" + _vm.Theme);
        SumPin.Text = Loc.Get(_vm.HasPin ? "Common_On" : "Common_Off");
    }

    /// <summary>The last page: the avatar and title rise in, the four summary rows stagger in, and the green check pops.</summary>
    private void PlayDone()
    {
        RefreshSummary();
        var dur = Anim.Dur("DurSlow");
        DoneCheckScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DoneCheckScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        DoneCheckScale.ScaleX = DoneCheckScale.ScaleY = 1;
        if (!Anim.MotionEnabled || !dur.HasTimeSpan || dur.TimeSpan == TimeSpan.Zero) return;

        Anim.FadeIn(DoneAvatarHost, 0, 12);
        Anim.FadeIn(DoneTitle, 1, 12);
        var rows = new UIElement[] { RowProfile, RowLanguage, RowTheme, RowPin };
        for (var i = 0; i < rows.Length; i++) Anim.FadeIn(rows[i], i + 2, 10);

        // The check pops in with a little overshoot once the avatar is there.
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var pop = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(dur.TimeSpan.TotalMilliseconds * 2)))
            {
                BeginTime = TimeSpan.FromMilliseconds(Anim.StaggerMs * 4),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.9 }
            };
            pop.Completed += (_, _) => { DoneCheckScale.BeginAnimation(prop, null); DoneCheckScale.SetValue(prop, 1.0); };
            DoneCheckScale.BeginAnimation(prop, pop);
        }
        DoneCheckScale.ScaleX = DoneCheckScale.ScaleY = 0;
    }

    // ---- hand-off from the greeting --------------------------------------------------------------------

    /// <summary>The card rises in over the fading greeting (opacity, 28 px up, 0.97 to 1). Instant under reduced motion.</summary>
    public void PlayIn()
    {
        Visibility = Visibility.Visible;
        CardHost.BeginAnimation(OpacityProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardMove.BeginAnimation(TranslateTransform.YProperty, null);
        CardHost.Opacity = 1;
        CardScale.ScaleX = CardScale.ScaleY = 1;
        CardMove.Y = 0;

        var dur = Anim.Dur("DurHero");
        if (IsLoaded && Anim.MotionEnabled && dur.HasTimeSpan && dur.TimeSpan > TimeSpan.Zero)
        {
            var delay = TimeSpan.FromMilliseconds(120);
            var fade = new DoubleAnimation(0, 1, dur) { BeginTime = delay, EasingFunction = Anim.EaseOut };
            fade.Completed += (_, _) => { CardHost.BeginAnimation(OpacityProperty, null); CardHost.Opacity = 1; };
            CardHost.Opacity = 0;
            CardHost.BeginAnimation(OpacityProperty, fade);

            CardMove.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(28, 0, dur) { BeginTime = delay, EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
            foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                CardScale.BeginAnimation(prop, new DoubleAnimation(0.97, 1, dur) { BeginTime = delay, EasingFunction = Anim.EaseOut });
        }
        Dispatcher.BeginInvoke(new Action(() => FocusPage(_shownIndex)), System.Windows.Threading.DispatcherPriority.Input);
    }
}
