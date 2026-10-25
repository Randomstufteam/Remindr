using Microsoft.Extensions.Hosting;
using Remindr.Data.Repositories;
using Remindr.Models;

namespace Remindr.Services;

/// <summary>
/// Fires alerts for every profile. Every 30 s it compares wall-clock "now" with each open reminder (no long timers, so sleep
/// and clock changes just work), asks <see cref="NotificationPlanner"/> what is due, writes the notification_log row FIRST and
/// only then shows the toast, so a crash between the two can lose one toast but never repeat it. Duplicates are prevented by
/// the planner reading the log each tick and ticks never overlapping (the UNIQUE constraint includes fired_at, so it only
/// catches same-second repeats). Stale occurrences and the per-tick cap are the planner's rules (StaleAfter, Budget).
/// The loop waits for <see cref="Release"/> first: a process started by a toast button click must stay silent until it knows
/// it is not about to exit.
/// </summary>
public sealed class SchedulerService : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly ReminderRepo _reminders;
    private readonly NotificationLogRepo _log;
    private readonly IToastSink _toasts;
    private readonly IClock _clock;
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _tickLock = new(1, 1);
    private DateOnly? _lastDay;

    public SchedulerService(ReminderRepo reminders, NotificationLogRepo log, IToastSink toasts, IClock clock)
    {
        _reminders = reminders;
        _log = log;
        _toasts = toasts;
        _clock = clock;
    }

    /// <summary>Raised (on the scheduler thread) once when the local date changes between two ticks.</summary>
    public event EventHandler? DayChanged;

    /// <summary>Raised (on the scheduler thread) at the end of every tick, so the tray can refresh its tooltip and alert dot.</summary>
    public event EventHandler? Ticked;

    /// <summary>Lets the loop start ticking. Called at normal start-up and once a toast-launched process decides to stay.</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>
    /// Hosting 8 calls ExecuteAsync synchronously from <c>IHost.Start</c>, i.e. on the WPF UI thread with its
    /// SynchronizationContext. Task.Run moves the whole loop to the pool, so DB work never runs on the UI thread and
    /// OnExit's blocking StopAsync cannot deadlock waiting for a continuation queued on the blocked UI thread.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => RunAsync(stoppingToken));

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _released.Task.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await TickAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { ErrorLog.Write(ex, "Scheduler tick"); }
                await Task.Delay(TickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>One pass over all profiles using the injected clock. Never overlaps itself; one reminder failing does not stop the rest.</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        await _tickLock.WaitAsync(ct);
        try
        {
            var nowLocal = _clock.Now;
            var today = DateOnly.FromDateTime(nowLocal);
            if (_lastDay is { } last && last != today) RaiseDayChanged();
            _lastDay = today;

            IReadOnlyList<Reminder> open;
            try { open = _reminders.ListOpenForScheduler(nowLocal); }
            catch (Exception ex) { ErrorLog.Write(ex, "Scheduler query"); return; }

            // nowLocal is local wall-clock time; nowUtc is UTC. Never swap them (the planner compares log times as elapsed time).
            var nowUtc = _clock.UtcNow;
            var pending = new List<PendingAlert>();
            foreach (var r in open)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var log = _log.Get(r.Id, NotificationPlanner.OccurrenceKey(r));
                    foreach (var alert in NotificationPlanner.GetDue(r, log, nowLocal, nowUtc, r.SnoozedUntilLocal))
                        pending.Add(new PendingAlert(r, alert));
                }
                catch (Exception ex) { ErrorLog.Write(ex, "Scheduler, reminder " + r.Id); }
            }

            // At most a few toasts per tick; a backlog of overdue alerts beyond that is logged silently.
            var (show, silence) = NotificationPlanner.Budget(pending);
            foreach (var p in silence)
            {
                try { _log.Insert(new NotificationLogEntry(p.Reminder.Id, p.Alert.Occurrence, p.Alert.Kind, nowUtc)); }
                catch (Exception ex) { ErrorLog.Write(ex, "Scheduler, reminder " + p.Reminder.Id); }
            }
            foreach (var p in show)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Log first: if showing crashes, the alert is lost rather than repeated every 30 s.
                    var written = _log.Insert(new NotificationLogEntry(p.Reminder.Id, p.Alert.Occurrence, p.Alert.Kind, nowUtc));
                    if (written) await _toasts.ShowAsync(p.Reminder, p.Alert, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { ErrorLog.Write(ex, "Scheduler, reminder " + p.Reminder.Id); }
            }
        }
        finally { _tickLock.Release(); }
        try { Ticked?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { ErrorLog.Write(ex, "Scheduler tick event"); }
    }

    private void RaiseDayChanged()
    {
        try { DayChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { ErrorLog.Write(ex, "Scheduler day change"); }
    }
}
