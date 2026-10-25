using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>Welcome, Options, Installing, Done (plus Error). The work itself is done by the <see cref="ISetupBackend"/>.</summary>
public sealed class InstallFlow
{
    private readonly MainWindow _w;
    private readonly ISetupBackend _b;
    private readonly ISetupLog _log;
    private ExistingInstall _existing = new ExistingInstall();
    private bool _desktop, _startup, _launch = true;

    public InstallFlow(MainWindow window, ISetupBackend backend, ISetupLog log)
    {
        _w = window;
        _b = backend;
        _log = log;
    }

    public void Start()
    {
        _existing = _b.Detect();
        if (_existing.Mode != InstallMode.Fresh)
        {
            _desktop = _existing.HasDesktopShortcut;
            _startup = SetupFeatures.StartWithWindows && _existing.HasRunValue;
        }
        _w.SetTitle(_b.IsDemo ? "Remindr Setup (demo)" : "Remindr Setup");
        ShowWelcome();
    }

    private string PrimaryVerb => _existing.Mode switch
    {
        InstallMode.Update => "Update to " + SetupInfo.Version,
        InstallMode.Reinstall => "Reinstall",
        InstallMode.Downgrade => "Install " + SetupInfo.Version,
        _ => "Install",
    };

    private FooterConfig Cancellable(string primary, Action onPrimary, string? back = null, Action? onBack = null) => new FooterConfig
    {
        Primary = primary, OnPrimary = onPrimary, Back = back, OnBack = onBack,
        Cancel = "Cancel", OnCancel = ConfirmCancel, OnClose = ConfirmCancel,
    };

    private void ConfirmCancel() =>
        _w.AskCancel("Cancel setup?", "Remindr has not been " + (_existing.Mode == InstallMode.Fresh ? "installed" : "changed") + " yet.", _w.Exit);

    private void ShowWelcome() =>
        _w.Navigate(new WelcomePage(_existing), Cancellable("Next", ShowOptions));

    private OptionsPage? _options;

    private void ShowOptions()
    {
        var page = new OptionsPage(_b.InstallDir, _existing, _desktop, _startup, _launch);
        _options = page;
        _w.Navigate(page, Cancellable(PrimaryVerb, BeginInstall, "Back", () => { Remember(); ShowWelcome(); }), page.FocusFirst);
    }

    private void Remember()
    {
        if (_options == null) return;
        _desktop = _options.Desktop;
        _startup = _options.Startup;
        _launch = _options.Launch;
    }

    private async void BeginInstall()
    {
        Remember();
        var verb = _existing.Mode == InstallMode.Update ? "Updating" : "Installing";
        var page = new ProgressPage(verb + " Remindr", "Getting ready…");
        _w.Navigate(page, new FooterConfig());          // nothing to click while files are changing

        var options = new InstallOptions { DesktopShortcut = _desktop, StartWithWindows = _startup };
        try
        {
            var started = DateTime.UtcNow;
            await Task.Run(() => _b.Install(options, _w.Prompts, p => _w.Dispatcher.BeginInvoke(new Action(() => page.Report(p)))));
            // Let the bar visibly finish even when everything was instant.
            var minimum = TimeSpan.FromMilliseconds(Motion.Enabled ? 1400 : 0);
            var left = minimum - (DateTime.UtcNow - started);
            page.SetFraction(1, true);
            await Task.Delay(left > TimeSpan.Zero ? left + TimeSpan.FromMilliseconds(300) : TimeSpan.FromMilliseconds(Motion.Enabled ? 350 : 0));
            ShowDone();
        }
        catch (SetupCancelledException ex)
        {
            _log.Info("Setup stopped: " + ex.Message);
            ShowError("Setup was stopped", "Remindr is still running, so it could not be " +
                (_existing.Mode == InstallMode.Fresh ? "installed" : "updated") + ". Close Remindr and try again.", ex);
        }
        catch (Exception ex)
        {
            _log.Error("Install failed.", ex);
            ShowError("Setup could not finish", FriendlyReason(ex) + " Nothing was changed.", ex);
        }
    }

    private void ShowError(string heading, string message, Exception ex)
    {
        var log = (_log as FileSetupLog)?.Path;
        _w.Navigate(new ErrorPage(heading, message, log), new FooterConfig
        {
            Primary = "Try again", OnPrimary = ShowOptions,
            Back = log == null ? null : "Open log", OnBack = () => OpenLog(log!),
            Cancel = "Close", OnCancel = _w.Exit, OnClose = _w.Exit,
        });
    }

    private void ShowDone()
    {
        var detail = _existing.Mode == InstallMode.Update
            ? "Remindr is now version " + SetupInfo.Version + ". Your reminders were kept."
            : "Remindr " + SetupInfo.Version + " is installed. You can find it in the Start menu.";
        var launchFirst = _launch;
        Action finish = _w.Exit;
        // Starting a freshly written exe can stall for seconds while Windows scans it: do it off the UI thread with the window hidden.
        Action launch = async () => { _w.Hide(); await Task.Run(_b.LaunchApp); _w.Exit(); };
        _w.Navigate(new DonePage(_existing.Mode == InstallMode.Update ? "Remindr is up to date" : "Remindr is ready", detail), new FooterConfig
        {
            Primary = launchFirst ? "Launch Remindr" : "Finish", OnPrimary = launchFirst ? launch : finish,
            Back = launchFirst ? "Finish" : "Launch Remindr", OnBack = launchFirst ? finish : launch,
            OnClose = finish,
        });
    }

    internal static string FriendlyReason(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return "Windows did not allow setup to write to the install folder.";
        if (ex is System.IO.IOException) return "A file could not be written. It may be in use by another program.";
        return "Something unexpected happened.";
    }

    internal static void OpenLog(string path)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true }); }
        catch { /* nothing more to do */ }
    }
}

