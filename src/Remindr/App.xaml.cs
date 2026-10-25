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

        ApplyReducedMotion();

        try
        {
            _host = Host.CreateDefaultBuilder()
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
            window.Show();
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

    private static void ConfigureServices(HostBuilderContext ctx, IServiceCollection s)
    {
        // Data
        s.AddSingleton<Db>();
        s.AddSingleton<DbInitializer>();
        s.AddSingleton<SettingsRepo>();
        s.AddSingleton<ProfileRepo>();
        s.AddSingleton<CategoryRepo>();
        s.AddSingleton<ReminderRepo>();

        // Services
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton<ThemeService>();
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
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}



