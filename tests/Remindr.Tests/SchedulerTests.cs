using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.Tests;

public class SchedulerTests
{
    private sealed class FakeSink : IToastSink
    {
        public readonly List<(int Id, NotificationKind Kind)> Shown = new();
        public readonly List<bool> LoggedBeforeShow = new();
        public NotificationLogRepo? Log;
        public int? ThrowForId;

        public Task ShowAsync(Reminder r, PlannedAlert a, CancellationToken ct)
        {
            // At show time the log row for this alert must already exist.
            if (Log is not null)
                LoggedBeforeShow.Add(Log.Get(r.Id, a.Occurrence).Any(e => e.Kind == a.Kind));
            if (r.Id == ThrowForId) throw new InvalidOperationException("boom");
            Shown.Add((r.Id, a.Kind));
            return Task.CompletedTask;
        }
    }

    private static (ReminderEnv E, SchedulerService S, FakeSink Sink, NotificationLogRepo Log) Make(DateTime now)
    {
        var e = new ReminderEnv(now);
        var log = new NotificationLogRepo(e.T.Db);
        var sink = new FakeSink { Log = log };
        return (e, new SchedulerService(e.Repo, log, sink, e.Clock), sink, log);
    }

    /// <summary>Ticks, then advances the clock, <paramref name="count"/> times.</summary>
    private static async Task Ticks(SchedulerService s, FakeClock c, int count)
    {
        for (var i = 0; i < count; i++) { await s.TickAsync(); c.Now += Half; }
    }

    private static readonly TimeSpan Half = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task NormalReminder_FiresOnceAcrossManyTicks()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        var r = e.Repo.Create(ReminderEnv.R("Dentist", "2026-10-24", "10:00"));
        await Ticks(s, e.Clock, 240); // two hours
        Assert.Equal(new[] { (r.Id, NotificationKind.AtTime) }, sink.Shown);
    }

    [Fact]
    public async Task Important_OneDayOut_GetsPre1DayNow()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 10, 0, 0));
        using var _e = e;
        var r = ReminderEnv.R("Tax", "2026-10-25", "09:30");
        r.IsImportant = true;
        var made = e.Repo.Create(r);
        await s.TickAsync();
        await s.TickAsync();
        Assert.Equal(new[] { (made.Id, NotificationKind.Pre1Day) }, sink.Shown);
    }

    [Fact]
    public async Task Important_AtTimeThenNagEvery10Min_DismissStops()
    {
        var (e, s, sink, log) = Make(new DateTime(2026, 10, 24, 10, 0, 0));
        using var _e = e;
        var r = ReminderEnv.R("Meds", "2026-10-24", "10:02");
        r.IsImportant = true;
        var made = e.Repo.Create(r);

        await Ticks(s, e.Clock, 4);                        // 10:00 .. 10:01:30, before the due time
        Assert.DoesNotContain(sink.Shown, x => x.Kind == NotificationKind.AtTime);
        sink.Shown.Clear();

        await Ticks(s, e.Clock, 240);                      // 10:02 .. 12:02
        Assert.Equal(NotificationKind.AtTime, sink.Shown[0].Kind);
        Assert.All(sink.Shown.Skip(1), x => Assert.Equal(NotificationKind.Nag, x.Kind));
        Assert.InRange(sink.Shown.Count(x => x.Kind == NotificationKind.Nag), 11, 13);

        log.Insert(new NotificationLogEntry(made.Id, NotificationPlanner.OccurrenceKey(made), NotificationKind.Dismissed, e.Clock.UtcNow));
        sink.Shown.Clear();
        await Ticks(s, e.Clock, 60);
        Assert.Empty(sink.Shown);
    }

    [Fact]
    public async Task AtTime_FiresAtTheMinute_NotBefore()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 14, 58, 0));
        using var _e = e;
        e.Repo.Create(ReminderEnv.R("Call", "2026-10-24", "15:00"));
        await Ticks(s, e.Clock, 4);                        // 14:58:00 .. 14:59:30
        Assert.Empty(sink.Shown);
        await Ticks(s, e.Clock, 2);                        // 15:00:00, 15:00:30
        Assert.Single(sink.Shown);
    }

    [Fact]
    public async Task Snooze5Min_FiresOnceAfterItExpires()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 10, 0, 0));
        using var _e = e;
        var r = e.Repo.Create(ReminderEnv.R("Tea", "2026-10-24", "10:00"));
        await s.TickAsync();
        Assert.Single(sink.Shown);
        sink.Shown.Clear();

        e.Repo.SetSnoozedUntil(r.Id, new DateTime(2026, 10, 24, 10, 6, 0));
        e.Clock.Now = new DateTime(2026, 10, 24, 10, 1, 0);
        await Ticks(s, e.Clock, 9);                        // 10:01 .. 10:05
        Assert.Empty(sink.Shown);
        await Ticks(s, e.Clock, 40);                       // 10:05 .. 10:25
        Assert.Equal(new[] { (r.Id, NotificationKind.Snooze) }, sink.Shown);
    }

    [Fact]
    public async Task CompletedReminder_NeverFires()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        var r = e.Repo.Create(ReminderEnv.R("Done", "2026-10-24", "10:00"));
        e.Repo.SetCompleted(r.Id, true);
        await Ticks(s, e.Clock, 240);
        Assert.Empty(sink.Shown);
    }

    [Fact]
    public async Task BothProfiles_Fire()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        var a = e.Repo.Create(ReminderEnv.R("A item", "2026-10-24", "09:00"));
        e.Session.SwitchTo(e.B);
        var b = e.Repo.Create(ReminderEnv.R("B item", "2026-10-24", "09:00"));
        e.Session.SwitchTo(e.A);
        await s.TickAsync();
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), sink.Shown.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task OneReminderThrowing_DoesNotBlockOthers()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        var bad = e.Repo.Create(ReminderEnv.R("Bad", "2026-10-24", "09:00"));
        var good = e.Repo.Create(ReminderEnv.R("Good", "2026-10-24", "09:00"));
        sink.ThrowForId = bad.Id;
        await s.TickAsync();
        Assert.Equal(new[] { (good.Id, NotificationKind.AtTime) }, sink.Shown);
    }

    [Fact]
    public async Task LogRowIsWrittenBeforeTheToastShows()
    {
        var (e, s, sink, log) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        var r = e.Repo.Create(ReminderEnv.R("X", "2026-10-24", "09:00"));
        await s.TickAsync();
        Assert.Equal(new[] { true }, sink.LoggedBeforeShow);
        Assert.Single(log.Get(r.Id, NotificationPlanner.OccurrenceKey(r)));
    }

    [Fact]
    public async Task DateChange_RaisesDayChangedOnce()
    {
        var (e, s, _, _) = Make(new DateTime(2026, 10, 24, 23, 59, 0));
        using var _e = e;
        var count = 0;
        s.DayChanged += (_, _) => count++;
        await s.TickAsync();                               // the first tick only records the day
        e.Clock.Now = new DateTime(2026, 10, 24, 23, 59, 30);
        await s.TickAsync();
        Assert.Equal(0, count);
        e.Clock.Now = new DateTime(2026, 10, 25, 0, 0, 0);
        await s.TickAsync();
        await s.TickAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Loop_WaitsForRelease_ThenTicks_AndStopsOnCancel()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        e.Repo.Create(ReminderEnv.R("X", "2026-10-24", "09:00"));
        await s.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        Assert.Empty(sink.Shown);                          // not released yet
        s.Release();
        for (var i = 0; i < 50 && sink.Shown.Count == 0; i++) await Task.Delay(100);
        Assert.Single(sink.Shown);
        await s.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UpgradeFrom03_FiftyOldOpenReminders_AtMostThreeToasts_NoEndlessNags()
    {
        // A 0.3.x database: lots of open reminders in the past and an empty notification_log.
        var now = new DateTime(2026, 10, 24, 12, 0, 0);
        var (e, s, sink, _) = Make(now);
        using var _e = e;
        for (var i = 0; i < 40; i++)
        {
            var r = ReminderEnv.R("Old " + i, now.AddDays(-2 - i).ToString("yyyy-MM-dd"), "08:00");
            r.IsImportant = i % 2 == 0;
            e.Repo.Create(r);
        }
        for (var i = 0; i < 10; i++)
            e.Repo.Create(ReminderEnv.R("This morning " + i, "2026-10-24", $"08:{i * 5:00}"));

        await Ticks(s, e.Clock, 240); // two hours
        Assert.InRange(sink.Shown.Count, 1, NotificationPlanner.MaxToastsPerTick);
        Assert.All(sink.Shown, x => Assert.Equal(NotificationKind.AtTime, x.Kind));
    }

    [Fact]
    public async Task Important_NagsStop24HoursAfterDue()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        var r = ReminderEnv.R("Meds", "2026-10-23", "10:00");
        r.IsImportant = true;
        e.Repo.Create(r);

        await Ticks(s, e.Clock, 121);                      // 09:00 .. 10:00 today, 23-24 h overdue
        Assert.Equal(NotificationKind.AtTime, sink.Shown[0].Kind);
        Assert.Equal(6, sink.Shown.Count(x => x.Kind == NotificationKind.Nag));
        sink.Shown.Clear();
        await Ticks(s, e.Clock, 240);                      // 10:00 .. 12:00, past 24 h
        Assert.Empty(sink.Shown);
    }

    [Fact]
    public async Task ManyDueAtOnce_OnTimeOnesAreDeferredNotDropped()
    {
        var (e, s, sink, _) = Make(new DateTime(2026, 10, 24, 9, 0, 0));
        using var _e = e;
        for (var i = 0; i < 5; i++) e.Repo.Create(ReminderEnv.R("Same minute " + i, "2026-10-24", "09:00"));
        await s.TickAsync();
        Assert.Equal(3, sink.Shown.Count);
        e.Clock.Now += Half;
        await s.TickAsync();
        Assert.Equal(5, sink.Shown.Count);
        Assert.Equal(5, sink.Shown.Select(x => x.Id).Distinct().Count());
    }
}
