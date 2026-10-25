using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Dialogs;
using Remindr.Views.Pages;

namespace Remindr;

public partial class App : Application
{
    private IHost? _host;
    private string? _baseFont;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Remindr.Views.Controls.ScrollWheelBubbler.Register();

        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception, "UI thread");
            MessageBox.Show(Loc.Format("Error_Unhandled", args.Exception.Message),
                            "Remindr", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ErrorLog.Write(ex, "Background");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write(args.Exception, "Task");
            args.SetObserved();
        };

        // One Remindr per user: a second launch hands over to the first and leaves before it builds anything.
        var launch = LaunchArgs.Parse(e.Args);
        _singleInstance = new SingleInstance(SingleInstance.DefaultId());
        if (!_singleInstance.TryBecomePrimary() && !HandOverToRunningInstance(launch, e.Args))
        {
            Shutdown(0);
            return;
        }
        _singleInstance.StartServer(OnSecondLaunch);

        NotificationService.RegisterAppId();
        ApplyReducedMotion();

        try
        {
            // Plain HostBuilder: no appsettings files, file watchers, env-var config or console/event-log loggers, which the app never uses.
            _host = new HostBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();

            _host.Services.GetRequiredService<DbInitializer>().Initialize();
            _host.Start();

            // Language before any view exists: saved choice, else the Windows display language, else English.
            var localization = _host.Services.GetRequiredService<LocalizationService>();
            localization.Initialize();
            localization.CultureChanged += (_, _) => ApplyLanguageToWindows(localization);

            // Apply saved theme before the window is shown.
            _host.Services.GetRequiredService<ThemeService>().ApplySaved();

            // Debug switch: show the greeting once at start-up (greeting only, no data changes).
            _host.Services.GetRequiredService<WelcomeController>().ShowOnStart = WelcomeController.HasWelcomeArg(e.Args);

            var window = _host.Services.GetRequiredService<MainWindow>();
            ApplyLanguageToWindows(localization);
            StartNotifications(window, launch);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Startup");
            MessageBox.Show(Loc.Format("Error_Startup", ex.Message), "Remindr", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// WPF controls format dates and numbers by their Language property, which does not follow the thread culture on its own.
    /// The main window (and with it the pages, dialogs and popups under it) is pointed at the current formatting culture.
    /// </summary>
    private void ApplyLanguageToWindows(LocalizationService localization)
    {
        var tag = localization.Current.IetfLanguageTag;
        if (string.IsNullOrEmpty(tag)) tag = "en-US";
        var language = System.Windows.Markup.XmlLanguage.GetLanguage(tag);
        var flow = localization.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        foreach (Window w in Windows)
        {
            w.Language = language;
            // The window, and with it the rail, pages, dialogs, popups and the picker overlay, follows the language's direction live.
            w.FlowDirection = flow;
            // The window keeps its normal font first (the Latin look is unchanged) and gains the language's fallback fonts after it.
            _baseFont ??= w.FontFamily.Source;
            w.FontFamily = new System.Windows.Media.FontFamily(FontPolicy.Chain(_baseFont, localization.Code));
        }
    }

    /// <summary>
    /// When Windows animations are off, zero every motion token before any view loads (takes effect after a restart):
    /// durations and stagger become 0 and decorative loops (blob drift) are told not to start via MotionEnabled.
    /// </summary>
    private void ApplyReducedMotion()
    {
        // REMINDR_REDUCED_MOTION=1 forces this path so it can be checked without touching the Windows animation setting.
        if (SystemParameters.ClientAreaAnimation && Environment.GetEnvironmentVariable("REMINDR_REDUCED_MOTION") != "1") return;
        foreach (var key in new[] { "DurFast", "DurNormal", "DurSlow", "DurShake", "DurHero", "DurHeroOut", "DurHue" })
            Resources[key] = new Duration(TimeSpan.Zero);
        Resources["StaggerMs"] = 0.0;
        Resources["MotionEnabled"] = false;
    }

    private TrayService? _tray;
    private SingleInstance? _singleInstance;
    private SchedulerService? _scheduler;
    private volatile MainWindow? _mainWindow;

    private bool _toastColdStart;

    /// <summary>How long a launch that got no answer waits for a closing copy (an update, Quit; OnExit may take ~5 s) to go away.</summary>
    private static readonly TimeSpan TakeOverWait = TimeSpan.FromSeconds(6);

    /// <summary>
    /// A later launch found this instance running (second process; nothing of the app has been built yet). Returns true when this
    /// process should start normally after all (the other copy was closing and is gone), false when it should exit.
    /// </summary>
    private bool HandOverToRunningInstance(LaunchArgs launch, IReadOnlyList<string> args)
    {
        // A toast click while Remindr runs reaches the running process through the toast library: forward nothing.
        var forward = SingleInstanceRules.ForDuplicate(launch) == DuplicateLaunchAction.ForwardAndExit;
        if (forward)
        {
            // Task.Run: blocking the UI thread on a continuation that wants this thread's context would deadlock.
            var delivered = Task.Run(() => _singleInstance!.SendToRunningAsync(args)).GetAwaiter().GetResult();
            if (delivered) return false;
        }
        // No answer, or a toast click Windows did not hand to a running copy: the other copy is most likely closing (the installer
        // starts the new version right after the old one quit). Wait for it to go and start normally instead of leaving nothing running.
        if (_singleInstance!.WaitToBecomePrimary(TakeOverWait)) return true;
        // Still there (frozen): tell a person who asked for the window, stay quiet at sign-in and for a toast click.
        if (forward && !launch.Minimized)
            MessageBox.Show(Loc.Get("App_AlreadyRunningStuck"), "Remindr", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    /// <summary>A second launch reached this instance (pool thread). Returns once the UI thread has handled it, which is the sender's "ok".</summary>
    private Task OnSecondLaunch(string[] args)
    {
        if (!SingleInstanceRules.ShouldBringForward(LaunchArgs.Parse(args))) return Task.CompletedTask;
        // Runs on the UI thread after start-up finished, when the window exists. During start-up the sender is not made to wait for it.
        var op = Dispatcher.InvokeAsync(() => { if (_mainWindow is { } w) BringForward(w); });
        return _mainWindow is null ? Task.CompletedTask : op.Task;
    }

    /// <summary>
    /// Any way the user opens the window (tray Open / Quick Add, a second launch, a toast body click) ends the toast cold-start
    /// wait: a later button click must not exit the app under the window they just opened.
    /// </summary>
    private void NoteWindowOpened()
    {
        if (!_toastColdStart) return;
        _toastColdStart = false;
        _scheduler?.Release();
    }

    /// <summary>
    /// Hooks up toast clicks, then shows the window. When a toast click started the app, the window stays hidden until the
    /// click arrives: a button click is applied and the app exits again; a body click shows the window. If no click arrives
    /// within a few seconds the window is shown anyway. With --minimized the window is created but only the tray icon shows.
    /// </summary>
    private void StartNotifications(MainWindow window, LaunchArgs launch)
    {
        _mainWindow = window;
        var notifications = _host!.Services.GetRequiredService<NotificationService>();
        var scheduler = _scheduler = _host.Services.GetRequiredService<SchedulerService>();
        var reminders = _host.Services.GetRequiredService<ReminderRepo>();
        _tray = new TrayService(window, _host.Services.GetRequiredService<TraySettings>(), notifications, BringForward);
        _tray.Start();
        // Logoff / shutdown must never be blocked by close-to-tray.
        SessionEnding += (_, _) => _tray.PrepareExit();
        // At midnight the date-based views (All Reminders grouping, calendar "today") reload through the normal change path.
        scheduler.DayChanged += (_, _) => Dispatcher.BeginInvoke(reminders.RaiseExternalRefresh);
        // Subscribe first: the library registers its COM activator as soon as it is touched (StartedFromToast does), and a
        // cold-start click that arrives before a handler is subscribed is dropped. The handler only reads _toastColdStart on
        // the UI thread, after this method has set it.
        notifications.Initialize(Dispatcher, outcome =>
        {
            var cold = _toastColdStart;
            _toastColdStart = false;
            // Only a body click raises the window; Complete/Snooze/Dismiss act quietly.
            if (outcome.ShowWindow) { BringForward(window); scheduler.Release(); }
            else if (cold) ExitApp();
        });
        _toastColdStart = NotificationService.StartedFromToast;
        // A process started by a toast click stays silent (no scheduler) until it knows it will not exit: a button click shuts it
        // down again, so the scheduler is released only for a body click or when no click arrived in time.
        if (!_toastColdStart)
        {
            // --minimized keeps the window hidden, but never when that would leave an unreachable app: the first-run welcome
            // needs the window, and so does a failed tray icon (there would be no way to open or quit).
            var firstRun = SetupViewModel.ShouldRun(_host.Services.GetRequiredService<ProfileRepo>());
            var stayHidden = launch.Minimized && !launch.Welcome && !firstRun && _tray.IsIconCreated;
            if (!stayHidden) window.Show();
            // The window's Loaded starts the update check; a window that stays hidden never loads, so start it here instead.
            else window.StartBackgroundWork();
            scheduler.Release();
            return;
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_toastColdStart) { _toastColdStart = false; BringForward(window); scheduler.Release(); }
        };
        timer.Start();
    }

    /// <summary>A real exit (update installer started, toast-launched action done): the window closes instead of hiding to the tray.</summary>
    internal static void ExitApp()
    {
        if (Current is App app) app._tray?.PrepareExit();
        Current.Shutdown();
    }

    internal static void BringForward(Window window)
    {
        (Current as App)?.NoteWindowOpened();
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static void ConfigureServices(HostBuilderContext ctx, IServiceCollection s)
    {
        // Data
        s.AddSingleton<Db>();
        s.AddSingleton<DbInitializer>();
        s.AddSingleton<SettingsRepo>();
        s.AddSingleton<ProfileRepo>();
        s.AddSingleton<CategoryRepo>();
        s.AddSingleton<ReminderRepo>();
        s.AddSingleton<NotificationLogRepo>();

        // Services
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton<ThemeService>();
        s.AddSingleton<ToastActionHandler>();
        s.AddSingleton<NotificationService>();
        s.AddSingleton<IToastSink>(sp => sp.GetRequiredService<NotificationService>());
        s.AddSingleton<TraySettings>();
        s.AddSingleton<IStartupRegistry, WindowsStartupRegistry>();
        s.AddSingleton<StartWithWindowsService>(sp => new StartWithWindowsService(sp.GetRequiredService<IStartupRegistry>(), sp.GetRequiredService<SettingsRepo>()));
        s.AddSingleton<SchedulerService>();
        s.AddHostedService(sp => sp.GetRequiredService<SchedulerService>());
        s.AddSingleton<LocalizationService>();
        s.AddSingleton<AvatarService>();
        s.AddSingleton<ProfileSession>();
        s.AddSingleton<WelcomeController>();
        s.AddSingleton<Wpf.Ui.IContentDialogService, Wpf.Ui.ContentDialogService>();
        s.AddSingleton<ProfileDialogs>();
        s.AddSingleton<ReminderDialogs>();
        s.AddSingleton<ProfilePickerViewModel>();
        s.AddSingleton(CreateUpdateService);
        s.AddSingleton<UpdateViewModel>();

        // Shell
        s.AddSingleton<MainWindow>();

        // Pages and view models
        s.AddSingleton<TodayPage>();
        s.AddSingleton<TodayViewModel>();
        s.AddSingleton<CalendarPage>();
        s.AddSingleton<CalendarViewModel>();
        s.AddSingleton<AllRemindersPage>();
        s.AddSingleton<AllRemindersViewModel>();
        s.AddSingleton<SettingsPage>();
        s.AddSingleton<SettingsViewModel>();
    }

    private static UpdateService CreateUpdateService(IServiceProvider sp)
    {
        Uri? feed = null;
        var loopback = false;
        IInstallerLauncher launcher = new ProcessInstallerLauncher();
#if DEBUG
        // Debug-only test hooks (compiled out of Release): REMINDR_UPDATE_FEED = a local JSON file or http://127.0.0.1:port/... feed,
        // REMINDR_UPDATE_NO_LAUNCH=1 = stop after verifying the download and only report "would launch <path>".
        var feedValue = Environment.GetEnvironmentVariable("REMINDR_UPDATE_FEED");
        if (!string.IsNullOrWhiteSpace(feedValue))
        {
            loopback = true;
            feed = System.IO.File.Exists(feedValue) ? new Uri(System.IO.Path.GetFullPath(feedValue)) : new Uri(feedValue);
        }
        if (Environment.GetEnvironmentVariable("REMINDR_UPDATE_NO_LAUNCH") == "1") launcher = new DryRunLauncher();
#endif
        var policy = new UpdatePolicy(allowLoopback: loopback);
        IUpdateClient client = new HttpUpdateClient(HttpUpdateClient.CreateHandler(), policy, "Remindr/" + AppInfo.Version);
#if DEBUG
        client = new FileFeedClient(client);
#endif
        return new UpdateService(client, sp.GetRequiredService<SettingsRepo>(), policy, launcher,
            sp.GetRequiredService<IClock>(), AppInfo.Version, UpdateService.DefaultTempDir, feed);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}



