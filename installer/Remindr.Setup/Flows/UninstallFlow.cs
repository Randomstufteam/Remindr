using System;
using System.Threading.Tasks;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>Confirm (with "Also remove my data"), Removing, Done (plus Error).</summary>
public sealed class UninstallFlow
{
    private readonly MainWindow _w;
    private readonly ISetupBackend _b;
    private readonly ISetupLog _log;
    private bool _removeData;

    public UninstallFlow(MainWindow window, ISetupBackend backend, ISetupLog log, bool removeDataPreset)
    {
        _w = window;
        _b = backend;
        _log = log;
        _removeData = removeDataPreset;
    }

    public void Start()
    {
        _w.SetTitle(_b.IsDemo ? "Remindr Uninstall (demo)" : "Remindr Uninstall");
        ShowConfirm();
    }

    private void ShowConfirm()
    {
        var page = new ConfirmUninstallPage(_b.DataDir);
        _w.Navigate(page, new FooterConfig
        {
            Primary = "Uninstall", OnPrimary = () => OnUninstallClicked(page),
            Cancel = "Cancel", OnCancel = _w.Exit, OnClose = _w.Exit,    // nothing has changed yet: no need to ask
        }, _w.FocusCancel);      // never the checkbox or Uninstall: a stray Space/Enter must not delete anything
    }

    private void OnUninstallClicked(ConfirmUninstallPage page)
    {
        if (!page.RemoveData)
        {
            _removeData = UninstallPolicy.ShouldRemoveData(false, false);
            BeginRemove();
            return;
        }
        // Wiping data needs a second, explicit yes. Cancel is focused; only the red button deletes.
        _w.Ask("Delete everything?",
            "Delete all your reminders, profiles and pictures? They are moved to the Recycle Bin; emptying it can't be undone.",
            "Cancel", "Delete everything", false, true, true, yes =>
            {
                if (!yes) { _w.FocusCancel(); return; }
                _removeData = UninstallPolicy.ShouldRemoveData(true, true);
                BeginRemove();
            }, danger: true);
    }

    private async void BeginRemove()
    {
        var page = new ProgressPage("Removing Remindr", "Getting ready…");
        _w.Navigate(page, new FooterConfig());
        var removeData = _removeData;
        try
        {
            var started = DateTime.UtcNow;
            await Task.Run(() => _b.Uninstall(removeData, _w.Prompts, p => _w.Dispatcher.BeginInvoke(new Action(() => page.Report(p)))));
            var left = TimeSpan.FromMilliseconds(Motion.Enabled ? 1400 : 0) - (DateTime.UtcNow - started);
            page.SetFraction(1, true);
            await Task.Delay(left > TimeSpan.Zero ? left + TimeSpan.FromMilliseconds(300) : TimeSpan.FromMilliseconds(Motion.Enabled ? 350 : 0));
            ShowDone(removeData);
        }
        catch (SetupCancelledException ex)
        {
            _log.Info("Uninstall stopped: " + ex.Message);
            ShowError("Uninstall was stopped", "Remindr is still running, so it could not be removed. Close Remindr and try again.");
        }
        catch (Exception ex)
        {
            _log.Error("Uninstall failed.", ex);
            ShowError("Remindr could not be fully removed", InstallFlow.FriendlyReason(ex) + " You can run uninstall again from Settings > Apps.");
        }
    }

    private void ShowError(string heading, string message)
    {
        var log = (_log as FileSetupLog)?.Path;
        _w.Navigate(new ErrorPage(heading, message, log), new FooterConfig
        {
            Primary = "Close", OnPrimary = _w.Exit,
            Back = log == null ? null : "Open log", OnBack = () => InstallFlow.OpenLog(log!),
            OnClose = _w.Exit,
        });
    }

    private void ShowDone(bool removedData)
    {
        var detail = removedData
            ? "Remindr was removed. Your data was moved to the Recycle Bin."
            : "Remindr was removed. Your reminders were kept in case you install it again.";
        _w.Navigate(new DonePage("Remindr was uninstalled", detail), new FooterConfig
        {
            Primary = "Finish", OnPrimary = _w.Exit, OnClose = _w.Exit,
        });
    }
}

