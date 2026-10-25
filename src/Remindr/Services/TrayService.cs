using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace Remindr.Services;

/// <summary>
/// The notification-area icon (Open / Quick Add / Quit) and the close-to-tray behaviour of the main window.
/// Decisions come from <see cref="TraySettings"/>; this class only wires them to the window and the icon.
/// </summary>
public sealed class TrayService : IDisposable
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    private readonly MainWindow _window;
    private readonly TraySettings _settings;
    private readonly NotificationService _notifications;
    private readonly Action<Window> _bringForward;
    private readonly Func<TrayStatus> _status;
    private TrayStatus _current = TrayStatus.None;
    private System.Drawing.Icon? _alertIcon;
    private TaskbarIcon? _icon;
    private MenuItem? _open, _quickAdd, _quit;
    private readonly QuitSignal _quitSignal = new();
    private bool _exiting;

    public TrayService(MainWindow window, TraySettings settings, NotificationService notifications, Action<Window> bringForward,
        Func<TrayStatus>? status = null)
    {
        _status = status ?? (() => TrayStatus.None);
        _window = window;
        _settings = settings;
        _notifications = notifications;
        _bringForward = bringForward;
    }

    /// <summary>True once the app is really exiting: closing the window must then not hide it.</summary>
    public bool IsExiting => _exiting;

    /// <summary>True when the notification-area icon exists (so a hidden window can be reopened and quit).</summary>
    public bool IsIconCreated => _icon is { IsCreated: true };

    public void Start()
    {
        _window.Closing += OnWindowClosing;
        // The installer (update or uninstall) asks a running Remindr to exit through this event, not through a window close.
        _quitSignal.Start(() => _window.Dispatcher.BeginInvoke(new Action(Quit)));
        Loc.CultureChanged += (_, _) => ApplyText();
        try
        {
            _open = new MenuItem(); _open.Click += (_, _) => _bringForward(_window);
            _quickAdd = new MenuItem(); _quickAdd.Click += (_, _) => _window.QuickAdd();
            _quit = new MenuItem(); _quit.Click += (_, _) => Quit();
            var menu = new ContextMenu();
            menu.Items.Add(_open); menu.Items.Add(_quickAdd); menu.Items.Add(new Separator()); menu.Items.Add(_quit);
            StyleMenu(menu);
            // The menu is not under the window, so it takes the window's reading direction and language when it opens.
            menu.Opened += (_, _) =>
            {
                menu.FlowDirection = _window.FlowDirection;
                menu.Language = _window.Language;
                FadeIn(menu);
            };

            Icon = LoadIcon(alert: false);
            _alertIcon = LoadIcon(alert: true);
            _icon = new TaskbarIcon
            {
                ContextMenu = menu,
                MenuActivation = PopupActivationMode.RightClick,
                NoLeftClickDelay = true,
                LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => _bringForward(_window)),
            };
            if (Icon is not null) _icon.Icon = Icon;
            ApplyText();
            _icon.ForceCreate(enablesEfficiencyMode: false);
            RefreshStatus();
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Creating the tray icon"); }
    }

    private System.Drawing.Icon? Icon { get; set; }

    private static System.Drawing.Icon? LoadIcon(bool alert)
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/remindr.ico"));
        return info is null ? null : TrayIcons.Create(info.Stream, alert);
    }

    /// <summary>Glass look (Remind UI 0.4): the app's menu styles. Falls back to the default look if the styles are missing.</summary>
    private static void StyleMenu(ContextMenu menu)
    {
        if (Application.Current?.TryFindResource("TrayContextMenu") is Style menuStyle) menu.Style = menuStyle;
        var item = Application.Current?.TryFindResource("TrayMenuItem") as Style;
        var separator = Application.Current?.TryFindResource("TrayMenuSeparator") as Style;
        foreach (var entry in menu.Items)
        {
            if (entry is MenuItem m && item is not null) m.Style = item;
            else if (entry is Separator sep && separator is not null) sep.Style = separator;
        }
    }

    /// <summary>The menu fades in over DurFast. Gate off: it just appears.</summary>
    private static void FadeIn(ContextMenu menu)
    {
        var dur = Views.Controls.Anim.Dur("DurFast");
        menu.BeginAnimation(UIElement.OpacityProperty, null);
        menu.Opacity = 1;
        if (!Views.Controls.Anim.EffectsEnabled || (dur.HasTimeSpan && dur.TimeSpan == TimeSpan.Zero)) return;
        menu.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur)
        {
            EasingFunction = Views.Controls.Anim.EaseOut
        });
    }

    /// <summary>
    /// Re-reads the numbers and updates the tooltip ("Remindr - 2 due today") and the alert dot. Safe to call from any thread and as often as
    /// wanted; the icon is only touched when something changed.
    /// </summary>
    public void RefreshStatus()
    {
        if (!_window.Dispatcher.CheckAccess()) { _window.Dispatcher.BeginInvoke(new Action(RefreshStatus)); return; }
        TrayStatus status;
        try { status = _status(); }
        catch (Exception ex) { ErrorLog.Write(ex, "Reading the tray status"); return; }
        if (_icon is null || status == _current) return;
        _current = status;
        ApplyText();
        var wanted = status.HasAlert ? _alertIcon : Icon;
        if (wanted is not null) _icon.Icon = wanted;
    }

    private void ApplyText()
    {
        if (_icon is not null) _icon.ToolTipText = _current.Tooltip;
        if (_open is not null) _open.Header = Loc.Get("Tray_Open");
        if (_quickAdd is not null) _quickAdd.Header = Loc.Get("Tray_QuickAdd");
        if (_quit is not null) _quit.Header = Loc.Get("Tray_Quit");
    }

    /// <summary>Marks the app as exiting so closing windows no longer hides them (Quit, session end, toast-launched exit).</summary>
    public void PrepareExit()
    {
        _exiting = true;
        DisposeIcon();
    }

    /// <summary>Menu Quit: removes the icon and exits the whole app.</summary>
    public void Quit()
    {
        PrepareExit();
        Application.Current.Shutdown();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Without a live tray icon a hidden window could never be brought back or quit: then a close is a real close.
        var decision = _settings.OnCloseRequested(_exiting || _icon is not { IsCreated: true });
        if (!decision.Hide) return;
        e.Cancel = true;
        _window.Hide();
        if (decision.ShowNotice) _notifications.ShowInfo(Loc.Get("Tray_StillRunningTitle"), Loc.Get("Tray_StillRunningBody"));
        // Nothing animates while hidden (the backdrop stops on IsVisible); give memory back too.
        _window.Dispatcher.BeginInvoke(new Action(TrimMemory), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(self.Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { /* best effort */ }
    }

    private void DisposeIcon()
    {
        try { _icon?.Dispose(); } catch { /* shutting down */ }
        _icon = null;
        _alertIcon?.Dispose();
        _alertIcon = null;
    }

    public void Dispose() { _exiting = true; _quitSignal.Dispose(); DisposeIcon(); }
}
