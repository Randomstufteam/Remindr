using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Remindr.Converters;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Views.Controls;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Dialogs;
using Remindr.Views.Pages;
using Wpf.Ui.Controls;

namespace Remindr;

public partial class MainWindow : FluentWindow
{
    /// <summary>When true, launching goes straight to the last profile if it has no PIN. Off: the user wants the picker on every launch (last profile is listed first).</summary>
    private const bool SkipPickerForUnlockedLastProfile = false;

    private readonly ProfileSession _session;
    private readonly ProfilePickerViewModel _picker;
    private readonly ProfileRepo _profiles;
    private readonly ThemeService _theme;
    private readonly ReminderDialogs _reminderDialogs;
    private readonly WelcomeController _welcome;
    private readonly LocalizationService _localization;
    private readonly UpdateService _update;
    private bool _firstRun;
    private bool _updateCheckStarted;

    public MainWindow(IServiceProvider services, ThemeService theme, ProfileSession session,
        ProfilePickerViewModel picker, ProfileRepo profiles, Wpf.Ui.IContentDialogService dialogService,
        ReminderDialogs reminderDialogs, WelcomeController welcome, LocalizationService localization,
        UpdateService update, UpdateViewModel updateViewModel)
    {
        _update = update;
        _localization = localization;
        _welcome = welcome;
        _session = session;
        _picker = picker;
        _profiles = profiles;
        _theme = theme;
        _reminderDialogs = reminderDialogs;

        InitializeComponent();
        ApplyNavItemTemplate();
        // Ctrl+N: new reminder from any page (not over the picker or another dialog).
        InputBindings.Add(new System.Windows.Input.KeyBinding(
            // AsyncRelayCommand (not fire-and-forget) so a failed save reaches DispatcherUnhandledException instead of vanishing.
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => _reminderDialogs.ShowEditAsync(null), CanAddReminder),
            System.Windows.Input.Key.N, System.Windows.Input.ModifierKeys.Control));
        FitToWorkArea();
        SizeChanged += (_, e) => { if (e.WidthChanged) ApplyResponsive(e.NewSize.Width); };
        theme.AttachWindow(this);

        // Page navigation: fade + slide over DurNormal (instant when Windows animations are off).
        RootNavigation.TransitionDuration = (int)Anim.Dur("DurNormal").TimeSpan.TotalMilliseconds;
        if (!Anim.MotionEnabled) RootNavigation.Transition = Wpf.Ui.Animations.Transition.None;

        dialogService.SetDialogHost(DialogHost);

        Picker.DataContext = picker;
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfilePickerViewModel.IsVisible)) UpdatePickerVisibility();
        };
        // Esc fallback: the picker's own KeyBinding needs keyboard focus inside it, which can lag (or miss) right after
        // it opens. Never steal Esc from a ContentDialog shown over the picker.
        PreviewKeyDown += (_, e) =>
        {
            // Welcome greeting: any key continues, even when keyboard focus has not reached it yet.
            if (Welcome.Visibility == Visibility.Visible && !Welcome.IsKeyboardFocusWithin && DialogHost.Content is null)
            {
                Welcome.TryDismissFromKey(e);
                return;
            }
            if (e.Key != System.Windows.Input.Key.Escape || !_picker.IsVisible || Picker.IsKeyboardFocusWithin) return;
            if (DialogHost.Content is not null || DialogHost.IsKeyboardFocusWithin) return;
            if (_picker.IsPinPromptOpen) _picker.CancelPinCommand.Execute(null);
            else if (_picker.CanClose) _picker.CloseCommand.Execute(null);
            else return;
            e.Handled = true;
        };
        session.CurrentProfileChanged += OnProfileChanged;
        welcome.ReplayRequested += (_, _) => ShowWelcome();
        Welcome.Continued += (_, _) => OnWelcomeContinued();
        Setup.Finished += (profile, addFirst) => OnSetupFinished(profile, addFirst);
        Setup.AlreadySetUp += OnSetupAlreadyDone;

        RootNavigation.SetServiceProvider(services);
        RootNavigation.Navigated += (_, e) => { DisablePageHostScrolling(); TrackPage(e.Page as FrameworkElement); };
        // Pages the navigation Frame has shown follow the window's direction and Language live (see TrackPage).
        foreach (var dp in new[] { FlowDirectionProperty, LanguageProperty })
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(dp, typeof(Window)).AddValueChanged(this, (_, _) => SyncPages());
        UpdateCard.DataContext = updateViewModel;
        // The card shows only while a profile is open: never over the welcome, setup, picker or PIN prompt (it waits until then).
        update.PropertyChanged += (_, _) => RefreshUpdateCard();
        session.CurrentProfileChanged += (_, _) => RefreshUpdateCard();
        foreach (var overlay in new FrameworkElement[] { Picker, Welcome, Setup })
            overlay.IsVisibleChanged += (_, _) => RefreshUpdateCard();
        // The installer was started (and verified): leave so it can replace the app files.
        update.InstallerStarted += (_, _) => App.ExitApp();
        Loaded += (_, _) =>
        {
            SquareContentFrameCorner();
            DisablePageHostScrolling();
            RootNavigation.Navigate(typeof(TodayPage));
            Dispatcher.BeginInvoke(new Action(() => ApplyResponsive(ActualWidth)), System.Windows.Threading.DispatcherPriority.Loaded);
            // Not during the first-run greeting or setup: that starts it when the setup is finished.
            if (!_firstRun) StartUpdateCheck();
        };

        StartProfileFlow();
    }

    // ---- Update check ---------------------------------------------------------------------------------------

    /// <summary>Once per launch, in the background, after the window is up. Fire and forget: the service logs its own failures.</summary>
    private void StartUpdateCheck()
    {
        if (_updateCheckStarted) return;
        _updateCheckStarted = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { _ = _update.RunStartupCheckAsync(); }
            catch (Exception ex) { ErrorLog.Write(ex, "Update check start"); }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// For a start that keeps the window hidden (--minimized), where Loaded does not fire until the user opens it: the once-per-launch
    /// automatic update check still runs (its card waits until a profile is open in the window).
    /// </summary>
    internal void StartBackgroundWork()
    {
        if (!_firstRun) StartUpdateCheck();
    }

    private bool _updateCardShown;

    /// <summary>Shows the update card (slides up and fades in) only while a profile is open and nothing covers the app.</summary>
    private void RefreshUpdateCard()
    {
        var show = _update.ShowBanner && _session.Current is not null && !_firstRun
            && Picker.Visibility != Visibility.Visible && Welcome.Visibility != Visibility.Visible && Setup.Visibility != Visibility.Visible;
        if (show == _updateCardShown) return;
        _updateCardShown = show;
        UpdateCard.BeginAnimation(UIElement.OpacityProperty, null);
        UpdateCardSlide.BeginAnimation(TranslateTransform.YProperty, null);
        if (!show) { UpdateCard.Visibility = Visibility.Collapsed; return; }
        UpdateCard.Visibility = Visibility.Visible;
        var dur = Anim.Dur("DurNormal");
        if (!IsLoaded || !Anim.MotionEnabled || !dur.HasTimeSpan || dur.TimeSpan <= TimeSpan.Zero) return;
        UpdateCard.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = Anim.EaseOut });
        UpdateCardSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, dur) { EasingFunction = Anim.EaseOut });
    }
    // ---- Direction and language of the pages ------------------------------------------------------------
    // The navigation Frame does not pass inherited values (FlowDirection, Language) on to its page reliably: a page first shown
    // after the window was already right-to-left (Arabic saved as the language) stays left-to-right, and after the Frame has
    // written a value onto a page, later language switches no longer reach it. So every page the Frame shows is given the
    // window's values explicitly, when it is shown and whenever the window's direction or Language changes.
    private readonly HashSet<FrameworkElement> _pages = new();

    private void TrackPage(FrameworkElement? page)
    {
        if (page is null) return;
        _pages.Add(page);
        SyncPage(page);
        // The Frame can write its own values just after this event; set ours again once it is done.
        Dispatcher.BeginInvoke(new Action(() => SyncPage(page)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SyncPage(FrameworkElement page)
    {
        if (page.FlowDirection != FlowDirection) page.FlowDirection = FlowDirection;
        if (!Equals(page.Language, Language)) page.Language = Language;
    }

    private void SyncPages()
    {
        foreach (var page in _pages) SyncPage(page);
    }

    /// <summary>
    /// WPF-UI's NavigationViewItem ends up with its own template at runtime even though RemindrNavItemStyle sets one, so the
    /// style's template is assigned directly (a local value that wins over the control's own).
    /// </summary>
    private void ApplyNavItemTemplate()
    {
        var style = (Style)FindResource("RemindrNavItemStyle");
        var template = style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == TemplateProperty)?.Value as System.Windows.Controls.ControlTemplate;
        if (template is null) return;
        foreach (var item in RootNavigation.MenuItems.OfType<RemindrNavItem>().Concat(RootNavigation.FooterMenuItems.OfType<RemindrNavItem>()))
            item.Template = template;
    }

    /// <summary>Tray Quick Add: shows the window, then opens the new-reminder dialog only when that is allowed (a profile is open, no PIN prompt or other overlay).</summary>
    public void QuickAdd()
    {
        App.BringForward(this);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (CanAddReminder()) _ = _reminderDialogs.ShowEditAsync(null);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool CanAddReminder() =>
        _session.Current is not null && !_picker.IsVisible && DialogHost.Content is null
        && Welcome.Visibility != Visibility.Visible && Setup.Visibility != Visibility.Visible;

    private void StartProfileFlow()
    {
        // No profiles: this is the first run. Greeting, then setup (which creates the profile); nothing is saved until it is finished.
        if (SetupViewModel.ShouldRun(_profiles))
        {
            BeginFirstRun();
            return;
        }
        var last = _session.LastProfileId is { } id ? _profiles.Get(id) : null;
#pragma warning disable CS0162 // unreachable while the const is false
        if (SkipPickerForUnlockedLastProfile && last is { HasPin: false })
            _session.SwitchTo(last);
        else
            _picker.Show();
#pragma warning restore CS0162
        UpdatePickerVisibility();
        if (_welcome.ShowOnStart) ShowWelcome();
    }

    // ---- Welcome greeting overlay -------------------------------------------------------------------------
    // A full-window overlay above the shell and the picker. Shown over the running app it brings its own backing; hiding it
    // restores whatever was underneath. Every fade is versioned, so a quick second request never leaves it half visible.
    private readonly Dictionary<FrameworkElement, int> _overlayVersions = new();

    /// <summary>Fades an overlay in or out (DurSlow in, DurNormal out); instant under reduced motion. <paramref name="done"/> runs when it has settled.</summary>
    private void FadeOverlay(FrameworkElement el, bool show, Action? done = null, double exitSlide = 0)
    {
        var version = _overlayVersions[el] = _overlayVersions.GetValueOrDefault(el) + 1;
        var dur = Anim.Dur(show ? "DurSlow" : "DurNormal");
        var animate = IsLoaded && Anim.MotionEnabled && dur.HasTimeSpan && dur.TimeSpan > TimeSpan.Zero;
        var from = el.Visibility == Visibility.Visible ? el.Opacity : 0.0;
        el.BeginAnimation(UIElement.OpacityProperty, null);

        if (show)
        {
            el.Opacity = animate ? from : 1;
            el.Visibility = Visibility.Visible;
            if (!animate) { done?.Invoke(); return; }
            var fade = new DoubleAnimation(from, 1, dur) { EasingFunction = Anim.EaseOut };
            fade.Completed += (_, _) =>
            {
                if (version != _overlayVersions[el]) return;
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = 1;
                done?.Invoke();
            };
            el.BeginAnimation(UIElement.OpacityProperty, fade);
            return;
        }

        void Finish()
        {
            el.Visibility = Visibility.Collapsed;
            el.Opacity = 1;
            if (exitSlide != 0) el.RenderTransform = Transform.Identity;
            done?.Invoke();
        }
        if (!animate || el.Visibility != Visibility.Visible) { Finish(); return; }
        el.Opacity = from;
        if (exitSlide != 0)
        {
            // Lifts away while fading (the first-run greeting handing over to the setup card).
            var lift = new TranslateTransform();
            el.RenderTransform = lift;
            lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, exitSlide, dur) { EasingFunction = Anim.EaseOut });
        }
        var fadeOut = new DoubleAnimation(from, 0, dur) { EasingFunction = Anim.EaseOut };
        fadeOut.Completed += (_, _) =>
        {
            if (version != _overlayVersions[el]) return;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            Finish();
        };
        el.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>Settings > Replay welcome and <c>--welcome</c>: the greeting only, over whatever is showing. No data changes.</summary>
    private void ShowWelcome()
    {
        if (Welcome.Visibility == Visibility.Visible) return;
        Welcome.UseBacking = true;
        RootNavigation.IsEnabled = false;
        SetNavLayerVisible(true);
        FadeOverlay(Welcome, true, () => SetNavLayerVisible(false));
    }

    // ---- First run: greeting, then setup ------------------------------------------------------------------

    private void BeginFirstRun()
    {
        _firstRun = true;
        var vm = new SetupViewModel(_profiles, _localization.Code, _theme.Current,
            previewLanguage: _localization.PreviewLanguage, previewTheme: _theme.Preview);
        Setup.Attach(vm);
        // Nothing sits under the greeting on a first run, so the window's own backdrop (Acrylic) shows through it.
        Welcome.UseBacking = false;
        RootNavigation.IsEnabled = false;
        SetNavLayerVisible(false);
        Welcome.Opacity = 1;
        Welcome.Visibility = Visibility.Visible;
    }

    private void OnSetupAlreadyDone()
    {
        _firstRun = false;
        SetNavLayerVisible(true);
        FadeOverlay(Setup, false, () =>
        {
            RootNavigation.IsEnabled = true;
            StartUpdateCheck();
            _picker.Show();
        });
    }

    private void OnSetupFinished(Profile profile, bool addFirstReminder)
    {
        _firstRun = false;
        // Enter the app as the new profile (this also applies its accent and fills the rail), then let the card leave.
        _session.SwitchTo(profile);
        SetNavLayerVisible(true);
        FadeOverlay(Setup, false, async () =>
        {
            RootNavigation.IsEnabled = true;
            StartUpdateCheck();
            if (!addFirstReminder) return;
            try { await _reminderDialogs.ShowEditAsync(null); }
            catch (Exception ex) { ErrorLog.Write(ex, "Add first reminder"); }
        });
    }

    private void OnWelcomeContinued()
    {
        if (_firstRun)
        {
            // Hand-off: the greeting lifts away while the setup card rises in.
            Setup.PlayIn();
            FadeOverlay(Welcome, false, exitSlide: -18);
            return;
        }
        if (!_picker.IsVisible) SetNavLayerVisible(true);
        FadeOverlay(Welcome, false, () =>
        {
            RootNavigation.IsEnabled = !_picker.IsVisible;
            if (_picker.IsVisible) Picker.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        });
    }

    // ---- Picker overlay: fade + scale (0.98 -> 1) in over DurNormal, back out over DurFast ---------------------------
    // The picker is transparent (blobs over the window backdrop), so the navigation layer under it is hidden once the
    // picker is fully in, and shown again as soon as it starts to leave. _pickerVersion guards every Completed
    // handler, so Esc / re-show in the middle of an animation never leaves it half-hidden or flickering.
    private int _pickerVersion;

    private void SetNavLayerVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Hidden;
        RootNavigation.Visibility = v;
        NavGlass.Visibility = v;
    }

    private void ResetPickerVisuals()
    {
        Picker.BeginAnimation(UIElement.OpacityProperty, null);
        Picker.Opacity = 1;
        Picker.RenderTransform = Transform.Identity;
    }

    private void UpdatePickerVisibility()
    {
        var show = _picker.IsVisible;
        RootNavigation.IsEnabled = !show;
        var version = ++_pickerVersion;
        var dur = Anim.Dur(show ? "DurNormal" : "DurFast");
        var animate = IsLoaded && Anim.MotionEnabled && dur.HasTimeSpan && dur.TimeSpan > TimeSpan.Zero;
        var wasShown = Picker.Visibility == Visibility.Visible;

        // Where the current animation (if any) has got to, so a reversal continues from there.
        var fromOpacity = wasShown ? Picker.Opacity : 0.0;
        // At rest the transform is Identity (not a ScaleTransform): that means fully in, i.e. scale 1, not 0.98.
        var fromScale = !wasShown ? 0.98 : Picker.RenderTransform is ScaleTransform st ? st.ScaleX : 1.0;
        Picker.BeginAnimation(UIElement.OpacityProperty, null);

        var scale = new ScaleTransform(1, 1);
        Picker.RenderTransformOrigin = new Point(0.5, 0.5);
        Picker.RenderTransform = scale;

        if (show)
        {
            SetNavLayerVisible(true);
            Picker.Visibility = Visibility.Visible;
            if (!animate)
            {
                ResetPickerVisuals();
                SetNavLayerVisible(false);
            }
            else
            {
                Picker.Opacity = fromOpacity;
                var fade = new DoubleAnimation(fromOpacity, 1, dur) { EasingFunction = Anim.EaseOut };
                fade.Completed += (_, _) =>
                {
                    if (version != _pickerVersion) return;
                    ResetPickerVisuals();
                    SetNavLayerVisible(false);
                };
                Picker.BeginAnimation(UIElement.OpacityProperty, fade);
                AnimateScale(scale, fromScale, 1, dur);
            }

            // Keyboard focus on the first tile so Tab/Enter/Esc work straight away
            // (focus would otherwise be left on the now-disabled navigation).
            Dispatcher.BeginInvoke(new Action(() =>
                    Picker.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First))),
                System.Windows.Threading.DispatcherPriority.Input);
            return;
        }

        SetNavLayerVisible(true);
        if (!wasShown) { ResetPickerVisuals(); return; }
        if (!animate)
        {
            Picker.Visibility = Visibility.Collapsed;
            ResetPickerVisuals();
            return;
        }

        Picker.Opacity = fromOpacity;
        var fadeOut = new DoubleAnimation(fromOpacity, 0, dur) { EasingFunction = Anim.EaseOut };
        fadeOut.Completed += (_, _) =>
        {
            if (version != _pickerVersion) return;
            Picker.Visibility = Visibility.Collapsed;
            ResetPickerVisuals();
        };
        Picker.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        AnimateScale(scale, fromScale, 0.98, dur);
    }

    private static void AnimateScale(ScaleTransform scale, double from, double to, Duration dur)
    {
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(prop, new DoubleAnimation(from, to, dur) { EasingFunction = Anim.EaseOut });
    }

    /// <summary>Profile picture (or the tinted person glyph) as the footer icon. NavigationViewItem.Icon only accepts an IconElement.</summary>
    private void UpdateNavIcon(Profile p)
    {
        ImageSource? picture = null;
        try
        {
            // Rendered at exactly the displayed size (the ImageIcon below is 20x20, the nav icon box): a downscale makes it soft.
            const double size = 20, ringWidth = 1.5;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var px = (int)Math.Ceiling(size * dpi);
            var image = AvatarCatalog.Load(p.Avatar, null, (int)Math.Ceiling((size - 2 * ringWidth) * dpi));
            if (image is not null)
            {
                var visual = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                var c = new Point(size / 2, size / 2);
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), null, c, size / 2, size / 2);
                    dc.PushClip(new EllipseGeometry(c, size / 2 - ringWidth, size / 2 - ringWidth));
                    dc.DrawImage(image, new Rect(ringWidth, ringWidth, size - 2 * ringWidth, size - 2 * ringWidth));
                    dc.Pop();
                    dc.DrawEllipse(null, new Pen(ColorUtil.Brush(p.Color), ringWidth), c, size / 2 - ringWidth / 2, size / 2 - ringWidth / 2);
                }
                var rtb = new RenderTargetBitmap(px, px, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                picture = rtb;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Nav profile icon");
        }

        ProfileNavItem.Icon = picture is not null
            ? new ImageIcon { Source = picture, Width = 20, Height = 20, SnapsToDevicePixels = true, FlowDirection = FlowDirection.LeftToRight }
            : new SymbolIcon { Symbol = SymbolRegular.PersonCircle24, Filled = true, FontSize = 16, Foreground = ColorUtil.Brush(p.Color) };
    }

    /// <summary>
    /// The WPF-UI content frame has an 8px top-left radius; the nav glass now ends exactly at its left edge, so a rounded
    /// corner would leave a notch of bare backdrop between the two surfaces. Matched by its template values; no-op if absent.
    /// </summary>
    private void SquareContentFrameCorner()
    {
        var frame = FindVisual<System.Windows.Controls.Border>(RootNavigation).FirstOrDefault(b =>
            b.CornerRadius == new CornerRadius(8, 0, 0, 0) && b.BorderThickness == new Thickness(1, 1, 0, 0));
        if (frame is not null) frame.CornerRadius = new CornerRadius(0);
    }
    /// <summary>
    /// WPF-UI hosts every page in a DynamicScrollViewer (vertical bar Auto). A ScrollViewer measures its content with unlimited height,
    /// so a page's own ScrollViewer was handed all the height it wanted, grew to its content and never scrolled (Settings was cut off;
    /// the page's viewer also ate the mouse wheel so the outer one never moved). Every Remindr page owns its scrolling, so the host
    /// viewer is switched to Disabled: the page then gets exactly the visible area and its own ScrollViewer scrolls (wheel, touchpad,
    /// bar, PageUp/PageDown, Tab-into-view). Re-applied after each navigation. No-op if the viewer is absent.
    /// </summary>
    private void DisablePageHostScrolling()
    {
        foreach (var host in FindVisual<System.Windows.Controls.ScrollViewer>(RootNavigation))
        {
            if (host.GetType().Name != "DynamicScrollViewer") continue;
            host.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
            host.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
        }
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        var p = _session.Current;
        if (p is null)
        {
            // Current profile was deleted: back to the picker.
            _picker.Show();
            return;
        }

        _theme.SetAccent(ColorUtil.Parse(p.Color));
        ProfileNavItem.Content = p.Name;
        UpdateNavIcon(p);
        // The renamed label is a fresh TextBlock; keep it hidden if the rail is collapsed.
        if (_navCollapsed)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var l in NavLabels()) { l.Opacity = 0; l.Visibility = Visibility.Collapsed; }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---- Sidebar collapse / expand ----------------------------------------------------------------
    // Width (OpenPaneLength) animates 200ms ease-out; labels fade 150ms. Collapse: both start together.
    // Expand: width first, then labels fade back in.
    // Pane grid widths; the visible glass rail is 8 wider (template margins): 240 / 56. Keep in sync with MainWindow.xaml.
    private const double OpenLength = 232, CompactLength = 48;
    private bool _navCollapsed;

    private void NavToggle_Click(object sender, RoutedEventArgs e)
    {
        // A manual toggle sticks until the window next crosses a size threshold (see ApplyResponsive).
        SetNavCollapsed(!_navCollapsed);
    }

    // ---- Laptop-friendly sizing ---------------------------------------------------------------------
    private const double NarrowBelow = 1000, WideAbove = 1100; // hysteresis for the auto-collapse
    private bool _narrow, _responsiveInit;

    /// <summary>Startup size: up to 1280x800, but never more than 90% of the work area; centred in it.</summary>
    private void FitToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(1280, wa.Width * 0.9));
        Height = Math.Max(MinHeight, Math.Min(800, wa.Height * 0.9));
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    private void ApplyResponsive(double width)
    {
        var narrow = _responsiveInit ? (_narrow ? width <= WideAbove : width < NarrowBelow) : width < NarrowBelow;
        var changed = !_responsiveInit || narrow != _narrow;
        _responsiveInit = true;
        _narrow = narrow;
        if (!changed) return;

        Application.Current.Resources["PagePadding"] = narrow
            ? (Thickness)Application.Current.FindResource("PagePaddingCompact")
            : new Thickness(32, 24, 32, 24); // = Tokens.xaml PagePadding
        if (_navCollapsed != narrow) SetNavCollapsed(narrow);
    }

    private void SetNavCollapsed(bool collapsed)
    {
        _navCollapsed = collapsed;
        var normal = (Duration)FindResource("DurNormal");
        var fast = (Duration)FindResource("DurFast");
        var ease = (System.Windows.Media.Animation.IEasingFunction)FindResource("EaseOut");

        RootNavigation.BeginAnimation(Wpf.Ui.Controls.NavigationView.OpenPaneLengthProperty,
            new System.Windows.Media.Animation.DoubleAnimation(collapsed ? CompactLength : OpenLength, normal)
            { EasingFunction = ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd });

        foreach (var label in NavLabels())
        {
            label.BeginAnimation(UIElement.OpacityProperty, null);
            if (collapsed)
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, fast);
                fade.Completed += (_, _) =>
                {
                    if (_navCollapsed) label.Visibility = Visibility.Collapsed; // hidden, not clipped mid-word
                };
                label.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                label.Visibility = Visibility.Visible;
                label.Opacity = 0;
                label.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1, fast) { BeginTime = normal.TimeSpan });
            }
        }
    }

    /// <summary>The text of every nav item (including the footer profile name), found in each item's visual tree.</summary>
    private IEnumerable<System.Windows.Controls.TextBlock> NavLabels()
    {
        foreach (var item in FindVisual<Wpf.Ui.Controls.NavigationViewItem>(RootNavigation))
            foreach (var tb in FindVisual<System.Windows.Controls.TextBlock>(item))
                if (!string.IsNullOrEmpty(tb.Text) && tb.Text == item.Content as string)
                    yield return tb;
    }

    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in FindVisual<T>(child)) yield return d;
        }
    }

    private void ProfileNavItem_Click(object sender, RoutedEventArgs e) => _picker.Show();
}



