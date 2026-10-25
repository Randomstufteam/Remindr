using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Controls;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>
/// What a reminder card does when used: writes through the repo, opens the edit/delete dialogs, and plays the card motion
/// around the data change: a completed card waits a moment (strike line, fade) before the list refreshes, and a deleted card
/// slides out before it is removed. With the effects gate off nothing waits and the data change happens at once.
/// Shared by All Reminders and Calendar.
/// </summary>
public sealed class ReminderActionHandler : IReminderActions
{
    private readonly ReminderRepo _repo;
    private readonly Func<Reminder, Task<bool>> _confirmDelete;
    private readonly Func<Reminder, Task<Reminder?>> _edit;
    private readonly Action _refresh;
    private readonly bool _refreshAfterImportant;
    private readonly bool _leavesOnComplete;
    private readonly HashSet<int> _deleting = new();
    private int _settleVersion;

    /// <param name="refresh">Rebuilds the owning list; called after a completion settles.</param>
    /// <param name="refreshAfterImportant">Also refresh after the flag is toggled (the calendar's day dots depend on it).</param>
    /// <param name="leavesOnComplete">True when completing moves the card to another list (All Reminders), so it fades out first.</param>
    public ReminderActionHandler(ReminderRepo repo, ReminderDialogs dialogs, Action refresh, bool refreshAfterImportant = false,
        bool leavesOnComplete = false)
        : this(repo, dialogs.ConfirmDeleteAsync, r => dialogs.ShowEditAsync(r), refresh, refreshAfterImportant, leavesOnComplete)
    {
    }

    public ReminderActionHandler(ReminderRepo repo, Func<Reminder, Task<bool>> confirmDelete,
        Func<Reminder, Task<Reminder?>> edit, Action refresh, bool refreshAfterImportant = false, bool leavesOnComplete = false)
    {
        _repo = repo;
        _confirmDelete = confirmDelete;
        _edit = edit;
        _refresh = refresh;
        _refreshAfterImportant = refreshAfterImportant;
        _leavesOnComplete = leavesOnComplete;
    }

    /// <summary>True while this handler is the one writing, so the owner's own change event does not rebuild its list.</summary>
    public bool IsWriting { get; private set; }

    /// <summary>The single effects gate. Read at the moment an effect starts, never cached.</summary>
    public Func<bool> EffectsEnabled { get; set; } = () => Anim.EffectsEnabled;

    /// <summary>The wait used for the settle and the exit. Tests replace it.</summary>
    public Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Called with the id of a card to scroll into view and glow (after a save). The owning list sets it.</summary>
    public Action<Reminder>? Saved { get; set; }

    /// <summary>How long a just-completed card stays in place (strike line, fade) before the list refreshes. Ignored (0) when the effects gate is off.</summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromMilliseconds(450);

    private TimeSpan? _exitDelay;

    /// <summary>How long a deleted card plays its slide-out before it is removed (DurExit). 0 when the effects gate is off.</summary>
    public TimeSpan ExitDelay
    {
        get => _exitDelay ?? Anim.Dur("DurExit").TimeSpan;
        set => _exitDelay = value;
    }

    private TimeSpan EffectiveSettle => EffectsEnabled() ? SettleDelay : TimeSpan.Zero;
    private TimeSpan EffectiveExit => EffectsEnabled() ? ExitDelay : TimeSpan.Zero;

    public void CompletedChanged(ReminderItemViewModel item)
    {
        Write(() => _repo.SetCompleted(item.Id, item.IsCompleted));
        // Leave the card where it is for a moment so the strike line is seen (it then fades out if it is about to change lists), then refresh.
        item.IsSettling = false;
        var token = ++item.SettleToken;
        var version = ++_settleVersion;
        // Only fade a card that is about to change groups (tick then quick untick ends where it started: no fade-out-and-back).
        var leaves = _leavesOnComplete && item.IsCompleted != item.ShownCompleted;
        _ = SettleAsync(version, leaves ? item : null, token);
    }

    private async Task SettleAsync(int version, ReminderItemViewModel? fading, int fadeToken)
    {
        try
        {
            var settle = EffectiveSettle;
            if (settle > TimeSpan.Zero)
            {
                var exit = fading is null ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Min(EffectiveExit.Ticks, settle.Ticks));
                if (exit > TimeSpan.Zero)
                {
                    await Delay(settle - exit);
                    if (fading!.SettleToken == fadeToken) fading.IsSettling = true;
                    await Delay(exit);
                }
                else
                    await Delay(settle);
            }
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
        _ = SettleAsync(version, null, 0);
    }

    public async Task EditAsync(ReminderItemViewModel item)
    {
        var saved = await _edit(item.Reminder);
        if (saved is not null) Saved?.Invoke(saved);
    }

    /// <summary>
    /// Asks, then slides the card out and deletes. A second request for the same card while one is running is ignored, and a failed
    /// delete puts the card back, so there is never a double delete or a ghost card.
    /// </summary>
    public async Task DeleteAsync(ReminderItemViewModel item)
    {
        if (!_deleting.Add(item.Id)) return;
        try
        {
            if (!await _confirmDelete(item.Reminder)) return;

            var exit = EffectiveExit;
            if (exit > TimeSpan.Zero)
            {
                item.IsExiting = true;
                await Delay(exit);
            }

            var deleted = false;
            try { deleted = _repo.Delete(item.Id); }
            catch (Exception ex) { ErrorLog.Write(ex, "Deleting reminder"); }

            if (!deleted)
            {
                // Failed, or it was already gone: bring the card back / let the list drop it.
                item.IsExiting = false;
                _refresh();
            }
            // On success the repo's Changed event has already made the owning list rebuild without the card.
        }
        catch (Exception ex)
        {
            item.IsExiting = false;
            ErrorLog.Write(ex, "Deleting reminder");
        }
        finally
        {
            _deleting.Remove(item.Id);
        }
    }

    private void Write(Action action)
    {
        IsWriting = true;
        try { action(); }
        catch (Exception ex) { ErrorLog.Write(ex, "Updating reminder"); }
        finally { IsWriting = false; }
    }
}
