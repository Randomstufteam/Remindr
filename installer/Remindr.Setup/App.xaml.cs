using System;
using System.Windows;
using Remindr.Setup.Core;

namespace Remindr.Setup;

public partial class App : Application
{
    private static ISetupLog _log = new NullLog();
    private System.Threading.Mutex? _mutex;
    private SetupArgs _args = new SetupArgs();
    private string? _selfDeletePath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _log = new FileSetupLog();
        _args = SetupArgs.Parse(e.Args, SetupInfo.ExePath);
        _log.Info("Setup started: " + string.Join(" ", e.Args) + " (" + SetupInfo.ExePath + ")");

        DispatcherUnhandledException += (_, a) =>
        {
            _log.Error("Unhandled UI exception.", a.Exception);
            MessageBox.Show("Something went wrong: " + a.Exception.Message + "\n\nDetails were saved to " + ((FileSetupLog)_log).Path,
                "Remindr Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            a.Handled = true;
            Shutdown(1);
        };

        ApplyReducedMotion();

        try
        {
            // The installed uninstall.exe runs from %TEMP% so it can delete its own folder.
            if (_args.Uninstall && !_args.Demo && !_args.FromTemp && !_args.Quiet && SelfDelete.TryRelaunchFromTemp(_args, _log))
            {
                Shutdown(0);
                return;
            }

            if (_args.Quiet && !_args.Demo)
            {
                Shutdown(QuietRunner.Run(_args, _log));
                return;
            }

            if (!_args.Demo)
            {
                _mutex = new System.Threading.Mutex(true, @"Local\Remindr-Setup", out var first);
                if (!first) { Shutdown(0); return; }          // another setup window is already open
            }

            ThemeSetup.Apply(this);
            ISetupBackend backend = _args.Demo ? new DemoBackend(_args.DemoUpdate) : new RealBackend(_log);
            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            if (_args.Uninstall) new UninstallFlow(window, backend, _log, _args.RemoveData).Start();
            else new InstallFlow(window, backend, _log).Start();

            if (_args.Uninstall && _args.FromTemp) _selfDeletePath = SetupInfo.ExePath;
        }
        catch (Exception ex)
        {
            _log.Error("Startup failed.", ex);
            MessageBox.Show("Setup could not start: " + ex.Message + "\n\nDetails were saved to " + ((FileSetupLog)_log).Path,
                "Remindr Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The temp copy of the uninstaller cannot delete itself while running: hand that to a detached cmd.
        if (_selfDeletePath != null || SelfDelete.InPlaceUninstallDone) SelfDelete.Schedule(SetupInfo.ExePath, _log);
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Same idea as the main app: zero every motion token before any view loads when Windows animations are off.</summary>
    private void ApplyReducedMotion()
    {
        if (SystemParameters.ClientAreaAnimation) return;
        foreach (var key in new[] { "DurFast", "DurNormal", "DurSlow", "DurShake", "DurEnter", "DurExit" })
            Resources[key] = new Duration(TimeSpan.Zero);
        Resources["StaggerMs"] = 0.0;
        Resources["StaggerRevealMs"] = 0.0;
        Resources["MotionEnabled"] = false;
    }
}
