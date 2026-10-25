using Remindr.Data.Repositories;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>
/// What a reminder card does when used: writes through the repo, opens the edit/delete dialogs, and (for completion)
/// waits a moment so the strike-through is seen before asking the list to refresh. Shared by All Reminders and Calendar.
/// </summary>
public sealed class ReminderActionHandler : IReminderActions
{
    private readonly ReminderRepo _repo;
    private readonly ReminderDialogs _dialogs;
    private readonly Action _refresh;
    private int _settleVersion;

    private readonly bool _refreshAfterImportant;

    /// <param name="refresh">Rebuilds the owning list; called after a completion settles.</param>
    /// <param name="refreshAfterImportant">Also refresh after the flag is toggled (the calendar's day dots depend on it).</param>
    public ReminderActionHandler(ReminderRepo repo, ReminderDialogs dialogs, Action refresh, bool refreshAfterImportant = false)
    {
        _refreshAfterImportant = refreshAfterImportant;
        _repo = repo;
        _dialogs = dialogs;
        _refresh = refresh;
    }

    /// <summary>True while this handler is the one writing, so the owner's own change event does not rebuild its list.</summary>
    public bool IsWriting { get; private set; }

    /// <summary>How long a just-completed card stays in place (showing the strike-through) before the list refreshes. The page sets 0 when animations are off.</summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromMilliseconds(450);

    public void CompletedChanged(ReminderItemViewModel item)
    {
        Write(() => _repo.SetCompleted(item.Id, item.IsCompleted));
        // Leave the card where it is for a moment so the strike-through/fade is seen, then refresh.
        var version = ++_settleVersion;
        _ = SettleAsync(version);
    }

    private async Task SettleAsync(int version)
    {
        try
        {
            if (SettleDelay > TimeSpan.Zero) await Task.Delay(SettleDelay);
            if (version == _settleVersion) _refresh();
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log here, otherwise the failure would only surface as an unobserved task.
            ErrorLog.Write(ex, "Refreshing reminders");
        }
    }

    public void ImportantChanged(ReminderItemViewModel item)
    {
        Write(() => _repo.SetImportant(item.Id, item.IsImportant));
        if (!_refreshAfterImportant) return;
        var version = ++_settleVersion;
        _ = SettleAsync(version);
    }

    public Task EditAsync(ReminderItemViewModel item) => _dialogs.ShowEditAsync(item.Reminder);

    public async Task DeleteAsync(ReminderItemViewModel item) => await _dialogs.ConfirmDeleteAsync(item.Reminder);

    private void Write(Action action)
    {
        IsWriting = true;
        try { action(); }
        catch (Exception ex) { ErrorLog.Write(ex, "Updating reminder"); }
        finally { IsWriting = false; }
    }
}
