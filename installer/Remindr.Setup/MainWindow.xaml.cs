using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>What the footer shows for the current page. Null text hides a button; null OnClose means "cannot cancel now".</summary>
public sealed class FooterConfig
{
    public string? Primary { get; set; }
    public Action? OnPrimary { get; set; }
    public string? Back { get; set; }
    public Action? OnBack { get; set; }
    public string? Cancel { get; set; }
    public Action? OnCancel { get; set; }
    /// <summary>Esc, the title-bar close button and Alt+F4.</summary>
    public Action? OnClose { get; set; }
}

/// <summary>Borderless glass shell: title bar, drifting blobs, a page host with slide+fade transitions, a footer and a question overlay.</summary>
public partial class MainWindow : Window
{
    private FooterConfig _footer = new FooterConfig();
    private UserControl? _current;
    private bool _allowClose;

    private Action<bool>? _overlayDone;
    private bool _overlayLeftValue, _overlayRightValue, _overlayEscLeft, _overlayEnterLeft;

    // Stray-keystroke guard: keys typed while the window is just being activated (it can grab focus while the person types elsewhere) are ignored.
    private static readonly TimeSpan KeyGuard = TimeSpan.FromMilliseconds(600);
    private DateTime _activatedAt = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        Prompts = new ShellPrompts(this);
        Loaded += (_, _) => { _activatedAt = DateTime.UtcNow; StartBlobs(); };
        Activated += (_, _) => _activatedAt = DateTime.UtcNow;
    }

    /// <summary>Puts keyboard focus on the footer Cancel button (the safe default for destructive pages).</summary>
    public void FocusCancel()
    {
        if (CancelButton.Visibility == Visibility.Visible) CancelButton.Focus();
    }

    public ISetupPrompts Prompts { get; }

    public void SetTitle(string title)
    {
        TitleText.Text = title;
        Title = title;
    }

    // ---------------------------------------------------------------- pages

    public void Navigate(UserControl page, FooterConfig footer, Action? focus = null)
    {
        var old = _current;
        _current = page;
        ApplyFooter(footer);
        PageHost.Children.Add(page);

        if (!Motion.Enabled)
        {
            if (old != null) PageHost.Children.Remove(old);
        }
        else
        {
            var enter = new TranslateTransform(old == null ? 0 : 40, old == null ? 12 : 0);
            page.RenderTransform = enter;
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Enter) { EasingFunction = Motion.EaseOut });
            enter.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(enter.X, 0, Motion.Enter) { EasingFunction = Motion.EaseOut });
            enter.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(enter.Y, 0, Motion.Enter) { EasingFunction = Motion.EaseOut });

            if (old != null)
            {
                old.IsHitTestVisible = false;
                var leave = new TranslateTransform();
                old.RenderTransform = leave;
                var fade = new DoubleAnimation(old.Opacity, 0, Motion.Exit) { EasingFunction = Motion.EaseInOut };
                fade.Completed += (_, _) => PageHost.Children.Remove(old);
                old.BeginAnimation(OpacityProperty, fade);
                leave.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -28, Motion.Exit) { EasingFunction = Motion.EaseInOut });
            }
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (focus != null) focus();
            else if (PrimaryButton.Visibility == Visibility.Visible) PrimaryButton.Focus();
            else if (BackButton.Visibility == Visibility.Visible) BackButton.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyFooter(FooterConfig f)
    {
        _footer = f;
        Apply(PrimaryButton, f.Primary);
        Apply(BackButton, f.Back);
        Apply(CancelButton, f.Cancel);
        CloseButton.IsEnabled = f.OnClose != null;
        CloseButton.Opacity = f.OnClose != null ? 1 : 0.4;
    }

    private static void Apply(Button b, string? text)
    {
        b.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        if (text == null) return;
        b.Content = text;
        AutomationProperties.SetName(b, text);
    }

    // ---------------------------------------------------------------- buttons and keys

    private void Primary_Click(object sender, RoutedEventArgs e) => _footer.OnPrimary?.Invoke();
    private void Back_Click(object sender, RoutedEventArgs e) => _footer.OnBack?.Invoke();
    private void Cancel_Click(object sender, RoutedEventArgs e) => (_footer.OnCancel ?? _footer.OnClose)?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => _footer.OnClose?.Invoke();
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DateTime.UtcNow - _activatedAt < KeyGuard)
        {
            e.Handled = true;      // swallowed before any button or checkbox sees it
            return;
        }
        var overlay = Overlay.Visibility == Visibility.Visible;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (overlay) FinishOverlay(_overlayEscLeft ? _overlayLeftValue : _overlayRightValue);
            else _footer.OnClose?.Invoke();
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            // A focused button handles Enter itself; everywhere else Enter means the primary action.
            if (Keyboard.FocusedElement is Button) return;
            e.Handled = true;
            if (overlay) FinishOverlay(_overlayEnterLeft ? _overlayLeftValue : _overlayRightValue);
            else if (PrimaryButton.Visibility == Visibility.Visible && PrimaryButton.IsEnabled) _footer.OnPrimary?.Invoke();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;                    // Alt+F4 behaves like Esc
            if (Overlay.Visibility != Visibility.Visible) _footer.OnClose?.Invoke();
        }
        base.OnClosing(e);
    }

    /// <summary>Closes the window and ends the program with no further questions.</summary>
    public void Exit()
    {
        _allowClose = true;
        Close();
    }

    // ---------------------------------------------------------------- question overlay

    /// <summary>
    /// Shows a two-button question. UI thread only. Enter = right button, Esc = the side given by escLeft.
    /// With danger, the right button is red and the LEFT one is focused and answers Enter (the safe choice).
    /// </summary>
    public void Ask(string title, string body, string left, string right, bool leftValue, bool rightValue, bool escLeft, Action<bool> done, bool danger = false)
    {
        _overlayEnterLeft = danger;
        if (danger)
        {
            OverlayRight.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            OverlayRight.Foreground = Brushes.White;
        }
        else
        {
            OverlayRight.ClearValue(BackgroundProperty);
            OverlayRight.ClearValue(ForegroundProperty);
        }
        OverlayTitle.Text = title;
        OverlayBody.Text = body;
        OverlayLeft.Content = left;
        OverlayRight.Content = right;
        AutomationProperties.SetName(OverlayLeft, left);
        AutomationProperties.SetName(OverlayRight, right);
        AutomationProperties.SetName(Overlay, title);
        _overlayLeftValue = leftValue;
        _overlayRightValue = rightValue;
        _overlayEscLeft = escLeft;
        _overlayDone = done;

        MainContent.IsEnabled = false;
        Overlay.Visibility = Visibility.Visible;
        if (Motion.Enabled) Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Fast) { EasingFunction = Motion.EaseOut });
        (danger ? OverlayLeft : OverlayRight).Focus();
        _activatedAt = DateTime.UtcNow;     // a stray Enter just as the question appears must not answer it
    }

    /// <summary>Standard "cancel setup?" question: Enter or Esc keeps going.</summary>
    public void AskCancel(string title, string body, Action onConfirmed)
    {
        Ask(title, body, "Yes, cancel", "Keep going", true, false, false, yes => { if (yes) onConfirmed(); });
    }

    private void OverlayLeft_Click(object sender, RoutedEventArgs e) => FinishOverlay(_overlayLeftValue);
    private void OverlayRight_Click(object sender, RoutedEventArgs e) => FinishOverlay(_overlayRightValue);

    private void FinishOverlay(bool value)
    {
        var done = _overlayDone;
        _overlayDone = null;
        Overlay.Visibility = Visibility.Collapsed;
        MainContent.IsEnabled = true;
        if (PrimaryButton.Visibility == Visibility.Visible) PrimaryButton.Focus();
        done?.Invoke(value);
    }

    /// <summary>Questions from the engine thread: blocks it until the person answers.</summary>
    private sealed class ShellPrompts : ISetupPrompts
    {
        private readonly MainWindow _w;
        public ShellPrompts(MainWindow w) { _w = w; }

        public bool AskCloseRunningApp() => Block("Remindr is running",
            "Setup needs to close Remindr first. Close it now? Anything you were typing may be lost.", "Not now", "Close Remindr");

        public bool AskForceClose() => Block("Remindr did not close",
            "Remindr did not close by itself. Force it to quit? Unsaved changes in the app will be lost.", "Not now", "Force close");

        private bool Block(string title, string body, string left, string right)
        {
            var result = false;
            using var gate = new ManualResetEventSlim(false);
            _w.Dispatcher.BeginInvoke(new Action(() =>
                _w.Ask(title, body, left, right, false, true, true, r => { result = r; gate.Set(); })));
            gate.Wait();
            return result;
        }
    }

    // ---------------------------------------------------------------- blobs

    private void StartBlobs()
    {
        if (!Motion.Enabled) return;       // reduced motion: static blobs
        Motion.Pop(Blobs, 0.9);
        Drift(Blob0, 46, 30, 19, 23);
        Drift(Blob1, -40, 38, 23, 17);
        Drift(Blob2, 52, -34, 27, 21);
        Drift(Blob3, -46, -30, 21, 29);
    }

    private static void Drift(Ellipse blob, double dx, double dy, double secondsX, double secondsY)
    {
        var t = (TranslateTransform)blob.RenderTransform;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, new Duration(TimeSpan.FromSeconds(secondsX)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, new Duration(TimeSpan.FromSeconds(secondsY)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }
}

