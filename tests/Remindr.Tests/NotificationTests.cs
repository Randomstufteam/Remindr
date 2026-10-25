using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.Tests;

public class ToastArgsTests
{
    [Theory]
    [InlineData(ToastAction.Open)]
    [InlineData(ToastAction.Complete)]
    [InlineData(ToastAction.Snooze)]
    [InlineData(ToastAction.Dismiss)]
    public void EncodeThenParse_RoundTrips(ToastAction action)
    {
        var a = new ToastArgs(action, 42, 7, "2026-10-25T15:30");
        Assert.True(ToastArgs.TryParse(a.Encode(), out var back));
        Assert.Equal(a, back);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a=nope&r=1&p=1&o=x")]
    [InlineData("a=complete&r=x&p=1&o=x")]
    [InlineData("a=complete&r=1&p=1")]
    public void TryParse_RejectsBadText(string? text) => Assert.False(ToastArgs.TryParse(text, out _));

    [Fact]
    public void TagAndGroup_FitToastLimits()
    {
        var a = new ToastArgs(ToastAction.Open, int.MaxValue, int.MaxValue, "2026-10-25T15:30");
        Assert.True(a.Tag.Length <= 64 && a.Group.Length <= 64);
        Assert.Equal(a.Tag, a.With(ToastAction.Complete).Tag);
    }
}

public class SnoozeMathTests
{
    [Fact]
    public void Timed_RoundsUpToTheNextMinute()
    {
        var now = new DateTime(2026, 10, 25, 10, 0, 20);
        Assert.Equal(new DateTime(2026, 10, 25, 10, 6, 0), ToastActionHandler.SnoozeUntil(SnoozeChoice.Minutes5, now));
        Assert.Equal(new DateTime(2026, 10, 25, 11, 1, 0), ToastActionHandler.SnoozeUntil(SnoozeChoice.Hour1, now));
        Assert.Equal(new DateTime(2026, 10, 25, 10, 5, 0),
            ToastActionHandler.SnoozeUntil(SnoozeChoice.Minutes5, new DateTime(2026, 10, 25, 10, 0, 0)));
    }

    [Theory]
    [InlineData("5m", SnoozeChoice.Minutes5)]
    [InlineData("1h", SnoozeChoice.Hour1)]
    [InlineData("tomorrow", SnoozeChoice.Tomorrow)]
    [InlineData(null, SnoozeChoice.Minutes5)]
    [InlineData("??", SnoozeChoice.Minutes5)]
    public void ParseChoice(string? id, SnoozeChoice expected) => Assert.Equal(expected, ToastActionHandler.ParseChoice(id));
}

public class ToastActionHandlerTests
{
    private static (ReminderEnv Env, ToastActionHandler Handler, NotificationLogRepo Log, Reminder R) Make(string date = "2026-10-24", string time = "15:00")
    {
        var env = new ReminderEnv(new DateTime(2026, 10, 24, 14, 0, 30));
        var log = new NotificationLogRepo(env.T.Db);
        var r = env.Repo.Create(ReminderEnv.R("Dentist", date, time));
        return (env, new ToastActionHandler(env.Repo, log, env.Clock), log, r);
    }

    private static ToastArgs Args(Reminder r, ToastAction a) =>
        new(a, r.Id, r.ProfileId, NotificationPlanner.OccurrenceKey(r));

    [Fact]
    public void Complete_MarksDone_AndRaisesClosed()
    {
        var (env, h, _, r) = Make();
        using var _ = env;
        Reminder? closed = null;
        env.Repo.Closed += (_, c) => closed = c;
        var o = h.Handle(Args(r, ToastAction.Complete));
        Assert.True(o.Found);
        Assert.True(env.Repo.Get(r.Id)!.IsCompleted);
        Assert.Equal(r.Id, closed?.Id);
    }

    [Fact]
    public void Complete_WorksForAnotherProfileThanTheCurrentOne()
    {
        var (env, h, _, r) = Make();
        using var _ = env;
        env.Session.SwitchTo(env.B);
        Assert.True(h.Handle(Args(r, ToastAction.Complete)).Found);
        env.Session.SwitchTo(env.A);
        Assert.True(env.Repo.Get(r.Id)!.IsCompleted);
    }

    [Fact]
    public void Snooze5m_SetsSnoozedUntil_AndDueIsUntouched()
    {
        var (env, h, _, r) = Make();
        using var _ = env;
        h.Handle(Args(r, ToastAction.Snooze), "5m");
        var got = env.Repo.Get(r.Id)!;
        Assert.Equal("2026-10-24T14:06", got.SnoozedUntil);
        Assert.Equal(r.DueDate, got.DueDate);
        Assert.Equal(new DateTime(2026, 10, 24, 14, 6, 0), got.SnoozedUntilLocal);
    }

    [Fact]
    public void Snooze1h_SetsSnoozedUntil()
    {
        var (env, h, _, r) = Make();
        using var _ = env;
        h.Handle(Args(r, ToastAction.Snooze), "1h");
        Assert.Equal("2026-10-24T15:01", env.Repo.Get(r.Id)!.SnoozedUntil);
    }

    [Fact]
    public void Tomorrow_MovesToTomorrowKeepingTime_AndClearsSnooze()
    {
        // Due 3 days ago and snoozed: "tomorrow" is relative to today, not to the old due date.
        var (env, h, _, r) = Make("2026-10-21", "15:00");
        using var _ = env;
        env.Repo.SetSnoozedUntil(r.Id, new DateTime(2026, 10, 24, 16, 0, 0));
        h.Handle(Args(r, ToastAction.Snooze), "tomorrow");
        var got = env.Repo.Get(r.Id)!;
        Assert.Equal(new DateOnly(2026, 10, 25), got.DueDate);
        Assert.Equal(new TimeOnly(15, 0), got.DueTime);
        Assert.Null(got.SnoozedUntil);
    }

    [Fact]
    public void Tomorrow_KeepsAllDay()
    {
        var (env, h, _, _) = Make();
        using var _ = env;
        var allDay = env.Repo.Create(ReminderEnv.R("Birthday", "2026-10-24"));
        h.Handle(Args(allDay, ToastAction.Snooze), "tomorrow");
        Assert.Null(env.Repo.Get(allDay.Id)!.DueTime);
    }

    [Fact]
    public void Tomorrow_NeverMovesALaterReminderEarlier()
    {
        var (env, h, _, r) = Make("2026-10-28", "15:00");
        using var _ = env;
        h.Handle(Args(r, ToastAction.Snooze), "tomorrow");
        Assert.Equal(new DateOnly(2026, 10, 28), env.Repo.Get(r.Id)!.DueDate);
    }

    [Fact]
    public void StaleToast_OfACompletedReminder_ChangesNothing()
    {
        var (env, h, _, r) = Make();
        using var _ = env;
        env.Repo.SetCompleted(r.Id, true);
        Assert.True(h.Handle(Args(r, ToastAction.Snooze), "tomorrow").Found);
        h.Handle(Args(r, ToastAction.Snooze), "5m");
        var got = env.Repo.Get(r.Id)!;
        Assert.Equal(r.DueDate, got.DueDate);
        Assert.Null(got.SnoozedUntil);
    }

    [Fact]
    public void Dismiss_WritesKind9_ForThatOccurrence_AndPlannerGoesQuiet()
    {
        var (env, h, log, r) = Make("2026-10-24", "14:00");
        using var _ = env;
        h.Handle(Args(r, ToastAction.Dismiss));
        var occ = NotificationPlanner.OccurrenceKey(r);
        var entries = log.Get(r.Id, occ);
        Assert.Single(entries);
        Assert.Equal(NotificationKind.Dismissed, entries[0].Kind);
        Assert.True(Math.Abs((entries[0].FiredAtUtc - env.Clock.UtcNow).TotalSeconds) < 1);
        Assert.Empty(NotificationPlanner.GetDue(r, entries, env.Clock.Now));
    }

    [Fact]
    public void Open_ChangesNothing_AndAsksForTheWindow()
    {
        var (env, h, log, r) = Make();
        using var _ = env;
        var o = h.Handle(Args(r, ToastAction.Open));
        Assert.True(o.ShowWindow);
        Assert.False(env.Repo.Get(r.Id)!.IsCompleted);
        Assert.Empty(log.Get(r.Id, NotificationPlanner.OccurrenceKey(r)));
    }

    [Fact]
    public void MissingReminder_IsReportedNotFound()
    {
        var (env, h, _, r) = Make();
        using var _ = env;
        env.Repo.Delete(r.Id);
        Assert.False(h.Handle(Args(r, ToastAction.Complete)).Found);
    }
}

public class SnoozeClearingTests
{
    [Fact]
    public void Update_ClearsSnooze_WhenDateOrTimeChanges_ButKeepsItOtherwise()
    {
        using var e = new ReminderEnv();
        var r = e.Repo.Create(ReminderEnv.R("Dentist", "2026-10-27", "15:00"));
        e.Repo.SetSnoozedUntil(r.Id, new DateTime(2026, 10, 27, 15, 5, 0));

        var rename = e.Repo.Get(r.Id)!;
        rename.Title = "Dentist!";
        e.Repo.Update(rename);
        Assert.Equal("2026-10-27T15:05", e.Repo.Get(r.Id)!.SnoozedUntil);

        var later = e.Repo.Get(r.Id)!;
        later.DueTime = new TimeOnly(16, 0);
        e.Repo.Update(later);
        Assert.Null(e.Repo.Get(r.Id)!.SnoozedUntil);

        e.Repo.SetSnoozedUntil(r.Id, new DateTime(2026, 10, 27, 16, 5, 0));
        var otherDay = e.Repo.Get(r.Id)!;
        otherDay.DueDate = new DateOnly(2026, 10, 28);
        e.Repo.Update(otherDay);
        Assert.Null(e.Repo.Get(r.Id)!.SnoozedUntil);

        e.Repo.SetSnoozedUntil(r.Id, new DateTime(2026, 10, 28, 16, 5, 0));
        var allDay = e.Repo.Get(r.Id)!;
        allDay.DueTime = null;
        e.Repo.Update(allDay);
        Assert.Null(e.Repo.Get(r.Id)!.SnoozedUntil);
    }

    [Fact]
    public void SetSnoozedUntil_Null_Clears()
    {
        using var e = new ReminderEnv();
        var r = e.Repo.Create(ReminderEnv.R("X", "2026-10-27", "15:00"));
        e.Repo.SetSnoozedUntil(r.Id, new DateTime(2026, 10, 27, 15, 5, 0));
        e.Repo.SetSnoozedUntil(r.Id, null);
        Assert.Null(e.Repo.Get(r.Id)!.SnoozedUntil);
    }

    [Fact]
    public void Delete_RaisesClosed()
    {
        using var e = new ReminderEnv();
        var r = e.Repo.Create(ReminderEnv.R("X", "2026-10-27", "15:00"));
        Reminder? closed = null;
        e.Repo.Closed += (_, c) => closed = c;
        e.Repo.Delete(r.Id);
        Assert.Equal(r.Id, closed?.Id);
    }
}

public class NotificationLogRepoTests
{
    [Fact]
    public void Insert_ThenGet_RoundTripsInOrder_AndFiltersByOccurrence()
    {
        using var e = new ReminderEnv();
        var log = new NotificationLogRepo(e.T.Db);
        var r = e.Repo.Create(ReminderEnv.R("X", "2026-10-27", "15:00"));
        var t0 = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
        Assert.True(log.Insert(new NotificationLogEntry(r.Id, "2026-10-27T15:00", NotificationKind.Nag, t0.AddMinutes(10))));
        Assert.True(log.Insert(new NotificationLogEntry(r.Id, "2026-10-27T15:00", NotificationKind.AtTime, t0)));
        Assert.True(log.Insert(new NotificationLogEntry(r.Id, "2026-10-28T15:00", NotificationKind.AtTime, t0)));

        var got = log.Get(r.Id, "2026-10-27T15:00");
        Assert.Equal(new[] { NotificationKind.AtTime, NotificationKind.Nag }, got.Select(x => x.Kind).ToArray());
        Assert.Equal(t0, got[0].FiredAtUtc);
        Assert.Equal(DateTimeKind.Utc, got[0].FiredAtUtc.Kind);
        Assert.Empty(log.Get(r.Id, "nope"));
    }

    [Fact]
    public void Insert_DuplicateIsIgnored()
    {
        using var e = new ReminderEnv();
        var log = new NotificationLogRepo(e.T.Db);
        var r = e.Repo.Create(ReminderEnv.R("X", "2026-10-27", "15:00"));
        var entry = new NotificationLogEntry(r.Id, "2026-10-27T15:00", NotificationKind.Dismissed, DateTime.UtcNow);
        Assert.True(log.Insert(entry));
        Assert.False(log.Insert(entry));
        Assert.Single(log.Get(r.Id, "2026-10-27T15:00"));
    }
}
