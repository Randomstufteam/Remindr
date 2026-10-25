using Remindr.Models;
using Remindr.Services;

namespace Remindr.Tests;

public class NotificationPlannerTests
{
    // Tests treat local == UTC (explicit nowUtc) so they do not depend on the machine time zone.
    private static readonly DateTime Due = new(2026, 10, 24, 15, 0, 0);
    private const string Occ = "2026-10-24T15:00";

    private static Reminder Rem(bool important = false, bool allDay = false, bool done = false) => new()
    {
        Id = 7, DueDate = new DateOnly(2026, 10, 24),
        DueTime = allDay ? null : new TimeOnly(15, 0),
        IsImportant = important, IsCompleted = done,
    };

    private static NotificationLogEntry L(NotificationKind k, DateTime at, string occ = Occ, long id = 7) =>
        new(id, occ, k, at);

    private static IReadOnlyList<PlannedAlert> Get(Reminder r, DateTime now, DateTime? snooze = null, params NotificationLogEntry[] log) =>
        NotificationPlanner.GetDue(r, log, now, now, snooze);

    private static NotificationKind? Kind(IReadOnlyList<PlannedAlert> a) => a.Count == 0 ? null : a[0].Kind;

    [Fact]
    public void OccurrenceKey_format_and_allday()
    {
        Assert.Equal(Occ, NotificationPlanner.OccurrenceKey(Rem()));
        Assert.Equal("2026-10-24T09:00", NotificationPlanner.OccurrenceKey(Rem(allDay: true)));
    }

    [Fact]
    public void Normal_fires_once_at_due()
    {
        Assert.Empty(Get(Rem(), Due.AddSeconds(-1)));
        var a = Get(Rem(), Due);
        Assert.Equal(NotificationKind.AtTime, Kind(a));
        Assert.False(a[0].IsOverdue);
        Assert.Equal(Occ, a[0].Occurrence);
        Assert.Empty(Get(Rem(), Due.AddMinutes(30), null, L(NotificationKind.AtTime, Due)));
    }

    [Fact]
    public void Normal_has_no_pre_alerts()
    {
        Assert.Empty(Get(Rem(), Due.AddMinutes(-30)));
        Assert.Empty(Get(Rem(), Due.AddHours(-5)));
    }

    [Fact]
    public void Completed_gives_nothing()
    {
        Assert.Empty(Get(Rem(true, done: true), Due.AddMinutes(5)));
        Assert.Empty(Get(Rem(true, done: true), Due.AddHours(-2)));
    }

    [Fact]
    public void Important_pre1day_then_pre1hour_then_attime()
    {
        var r = Rem(true);
        Assert.Empty(Get(r, Due.AddHours(-24).AddSeconds(-1)));
        Assert.Equal(NotificationKind.Pre1Day, Kind(Get(r, Due.AddHours(-24))));
        var d1 = L(NotificationKind.Pre1Day, Due.AddHours(-24));
        Assert.Empty(Get(r, Due.AddHours(-10), null, d1));
        Assert.Equal(NotificationKind.Pre1Hour, Kind(Get(r, Due.AddHours(-1), null, d1)));
        var h1 = L(NotificationKind.Pre1Hour, Due.AddHours(-1));
        Assert.Empty(Get(r, Due.AddMinutes(-10), null, d1, h1));
        Assert.Equal(NotificationKind.AtTime, Kind(Get(r, Due, null, d1, h1)));
    }

    [Fact]
    public void Pre1day_not_fired_inside_hour_window_after_pre1hour_logged()
    {
        var r = Rem(true);
        Assert.Empty(Get(r, Due.AddMinutes(-30), null, L(NotificationKind.Pre1Hour, Due.AddMinutes(-40))));
    }

    [Fact]
    public void Nag_every_10_minutes_until_dismissed()
    {
        var r = Rem(true);
        var at = L(NotificationKind.AtTime, Due);
        Assert.Empty(Get(r, Due.AddMinutes(9), null, at));
        Assert.Equal(NotificationKind.Nag, Kind(Get(r, Due.AddMinutes(10), null, at)));
        var n1 = L(NotificationKind.Nag, Due.AddMinutes(10));
        Assert.Empty(Get(r, Due.AddMinutes(19), null, at, n1));
        Assert.Equal(NotificationKind.Nag, Kind(Get(r, Due.AddMinutes(20), null, at, n1)));
        var dis = L(NotificationKind.Dismissed, Due.AddMinutes(21));
        Assert.Empty(Get(r, Due.AddMinutes(60), null, at, n1, dis));
    }

    [Fact]
    public void Dismissed_stops_everything_even_before_attime()
    {
        Assert.Empty(Get(Rem(true), Due.AddMinutes(5), null, L(NotificationKind.Dismissed, Due)));
    }

    [Fact]
    public void Missed_prealerts_skipped_after_wake_and_overdue_flagged()
    {
        var a = Get(Rem(true), Due.AddMinutes(45));
        Assert.Single(a);
        Assert.Equal(NotificationKind.AtTime, a[0].Kind);
        Assert.True(a[0].IsOverdue);
    }

    [Fact]
    public void Overdue_threshold_is_one_minute()
    {
        Assert.False(Get(Rem(), Due.AddMinutes(1))[0].IsOverdue);
        Assert.True(Get(Rem(), Due.AddMinutes(1).AddSeconds(1))[0].IsOverdue);
    }

    [Fact]
    public void Pre1day_skipped_when_woken_inside_hour_window()
    {
        Assert.Equal(NotificationKind.Pre1Hour, Kind(Get(Rem(true), Due.AddMinutes(-20))));
    }

    [Fact]
    public void Snooze_suppresses_then_fires_once_normal_reminder()
    {
        var r = Rem();
        var until = Due.AddMinutes(30);
        Assert.Empty(Get(r, Due.AddMinutes(10), until));
        Assert.Equal(NotificationKind.Snooze, Kind(Get(r, until, until)));
        var s = L(NotificationKind.Snooze, until);
        Assert.Empty(Get(r, until.AddMinutes(30), until, L(NotificationKind.AtTime, Due), s));
    }

    [Fact]
    public void Snooze_then_nag_resumes_for_important()
    {
        var r = Rem(true);
        var until = Due.AddMinutes(30);
        var at = L(NotificationKind.AtTime, Due);
        var s = L(NotificationKind.Snooze, until);
        Assert.Empty(Get(r, until.AddMinutes(9), until, at, s));
        Assert.Equal(NotificationKind.Nag, Kind(Get(r, until.AddMinutes(10), until, at, s)));
    }

    [Fact]
    public void Old_nags_do_not_trigger_immediate_nag_after_snooze_fired()
    {
        var r = Rem(true);
        var until = Due.AddMinutes(60);
        var log = new[] { L(NotificationKind.AtTime, Due), L(NotificationKind.Nag, Due.AddMinutes(10)),
                          L(NotificationKind.Snooze, until) };
        Assert.Empty(Get(r, until.AddMinutes(1), until, log));
    }

    [Fact]
    public void Resnooze_needs_new_snooze_entry()
    {
        var r = Rem();
        var oldSnooze = L(NotificationKind.Snooze, Due.AddMinutes(30));
        var until2 = Due.AddMinutes(90);
        Assert.Equal(NotificationKind.Snooze,
            Kind(Get(r, until2, until2, L(NotificationKind.AtTime, Due), oldSnooze)));
    }

    [Fact]
    public void Snooze_before_due_suppresses_prealerts_and_does_not_replace_attime()
    {
        var r = Rem(true);
        var until = Due.AddHours(-3);
        Assert.Empty(Get(r, Due.AddHours(-5), until));
        Assert.Equal(NotificationKind.Snooze, Kind(Get(r, until, until)));
        var s = L(NotificationKind.Snooze, until);
        Assert.Empty(Get(r, until.AddHours(1), until, s)); // snooze inside the 24h window counts as Pre1Day
        Assert.Equal(NotificationKind.Pre1Hour, Kind(Get(r, Due.AddHours(-1), until, s)));
        Assert.Equal(NotificationKind.AtTime, Kind(Get(r, Due, until, s)));
    }

    [Fact]
    public void Snooze_fired_inside_hour_window_counts_as_pre1hour()
    {
        var r = Rem(true);
        var until = Due.AddMinutes(-40);
        var log = new[] { L(NotificationKind.Pre1Day, Due.AddHours(-24)), L(NotificationKind.Snooze, until) };
        Assert.Empty(Get(r, until.AddSeconds(30), until, log));
        Assert.Equal(NotificationKind.AtTime, Kind(Get(r, Due, until, log)));
    }

    [Fact]
    public void Pre1day_fired_before_window_edge_then_pre1hour_in_next_tick()
    {
        var r = Rem(true);
        var d1 = L(NotificationKind.Pre1Day, Due.AddHours(-1).AddSeconds(-10));
        Assert.Equal(NotificationKind.Pre1Hour, Kind(Get(r, Due.AddHours(-1).AddSeconds(20), null, d1)));
    }

    [Fact]
    public void Long_downtime_gives_one_nag_not_a_burst()
    {
        var r = Rem(true);
        var at = L(NotificationKind.AtTime, Due);
        var wake = Due.AddHours(5);
        var a = Get(r, wake, null, at);
        Assert.Single(a);
        Assert.Equal(NotificationKind.Nag, a[0].Kind);
        var n = L(NotificationKind.Nag, wake);
        Assert.Empty(Get(r, wake.AddSeconds(30), null, at, n));
        Assert.Empty(Get(r, wake.AddMinutes(9), null, at, n));
        Assert.Equal(NotificationKind.Nag, Kind(Get(r, wake.AddMinutes(10), null, at, n)));
    }

    [Fact]
    public void Snoozed_past_due_without_attime_fires_snooze_then_nags()
    {
        var r = Rem(true);
        var until = Due.AddMinutes(20);
        Assert.Empty(Get(r, Due, until)); // AtTime suppressed while snoozed
        var s = L(NotificationKind.Snooze, until);
        Assert.Empty(Get(r, until.AddMinutes(5), until, s)); // snooze stands in for AtTime
        Assert.Equal(NotificationKind.Nag, Kind(Get(r, until.AddMinutes(10), until, s)));
    }

    [Fact]
    public void AllDay_uses_0900()
    {
        var r = Rem(allDay: true);
        var nine = new DateTime(2026, 10, 24, 9, 0, 0);
        Assert.Empty(Get(r, nine.AddMinutes(-1)));
        var a = Get(r, nine);
        Assert.Equal(NotificationKind.AtTime, Kind(a));
        Assert.Equal("2026-10-24T09:00", a[0].Occurrence);
    }

    [Fact]
    public void Other_occurrence_and_other_reminder_log_ignored()
    {
        var r = Rem(true);
        var stale = new[]
        {
            L(NotificationKind.Dismissed, Due.AddDays(-1), "2026-10-23T15:00"),
            L(NotificationKind.AtTime, Due, "2026-10-23T15:00"),
            L(NotificationKind.Dismissed, Due, Occ, id: 99),
        };
        Assert.Equal(NotificationKind.AtTime, Kind(Get(r, Due, null, stale)));
    }

    [Fact]
    public void Only_one_alert_per_call()
    {
        Assert.Single(Get(Rem(true), Due.AddHours(-2)));
        Assert.Single(Get(Rem(true), Due.AddHours(3)));
    }

    [Fact]
    public void Utc_log_compared_by_elapsed_time_with_offset_between_local_and_utc()
    {
        // Local is UTC+5 here; log time is given in UTC.
        var r = Rem(true);
        var nowLocal = Due.AddMinutes(10);
        var nowUtc = nowLocal.AddHours(-5);
        var log = new[] { L(NotificationKind.AtTime, Due.AddHours(-5)) };
        Assert.Equal(NotificationKind.Nag, Kind(NotificationPlanner.GetDue(r, log, nowLocal, nowUtc)));
        Assert.Empty(NotificationPlanner.GetDue(r, log, Due.AddMinutes(9), Due.AddMinutes(9).AddHours(-5)));
    }

    [Fact]
    public void Three_arg_overload_works_in_system_zone()
    {
        var now = DateTime.SpecifyKind(Due, DateTimeKind.Local);
        var a = NotificationPlanner.GetDue(Rem(), Array.Empty<NotificationLogEntry>(), now);
        Assert.Equal(NotificationKind.AtTime, Kind(a));
    }
}
