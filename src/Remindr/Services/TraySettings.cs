using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>What happens when the window is asked to close.</summary>
public sealed record CloseDecision(bool Hide, bool ShowNotice);

/// <summary>
/// The close-to-tray setting and the rules around it. No UI, so it is unit tested without a tray.
/// Stored in global app_settings: <c>close_to_tray</c> (default on) and <c>tray_notice_shown</c>.
/// </summary>
public sealed class TraySettings
{
    public const string CloseToTrayKey = "close_to_tray";
    public const string NoticeShownKey = "tray_notice_shown";

    private readonly SettingsRepo _settings;

    public TraySettings(SettingsRepo settings) => _settings = settings;

    /// <summary>On unless the user turned it off (a missing value means on, so alerts keep coming).</summary>
    public bool CloseToTray
    {
        get
        {
            try { return _settings.Get(CloseToTrayKey) != "0"; }
            catch (Exception ex) { ErrorLog.Write(ex, "Reading close_to_tray"); return true; }
        }
        set
        {
            try { _settings.Set(CloseToTrayKey, value ? "1" : "0"); }
            catch (Exception ex) { ErrorLog.Write(ex, "Saving close_to_tray"); }
        }
    }

    /// <summary>
    /// Decides a close request. A real exit (Quit, session end, toast-launched shutdown) or the setting being off lets the
    /// window close. Otherwise it hides, and the first hide ever asks for the "still running" notice (remembered at once).
    /// </summary>
    public CloseDecision OnCloseRequested(bool exitRequested)
    {
        if (exitRequested || !CloseToTray) return new CloseDecision(false, false);
        var first = false;
        try
        {
            first = _settings.Get(NoticeShownKey) != "1";
            if (first) _settings.Set(NoticeShownKey, "1");
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Saving tray_notice_shown"); }
        return new CloseDecision(true, first);
    }
}
