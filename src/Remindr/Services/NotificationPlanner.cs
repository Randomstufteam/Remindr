using System.Globalization;
using Remindr.Models;

namespace Remindr.Services;

/// <summary>Values match notification_log.kind.</summary>
public enum NotificationKind { Pre1Day = 0, Pre1Hour = 1, AtTime = 2, Nag = 3, Snooze = 4, Dismissed = 9 }

/// <param name="FiredAtUtc">UTC time the entry was written.</param>
public sealed record NotificationLogEntry(long ReminderId, string Occurrence, NotificationKind Kind, DateTime FiredAtUtc);

public sealed record PlannedAlert(NotificationKind Kind, string Occurrence, bool IsOverdue);

/// <summary>
/// Pure notification rules. No clock access, no WPF: callers pass "now".
/// Log times are UTC; they are compared with "now" as elapsed time (nowUtc - firedUtc),
/// and mapped onto the local axis as nowLocal - elapsed, so no time zone lookup happens here.
/// </summary>
public static class NotificationPlanner
{
    /// <summary>Nominal time used for all-day reminders.</summary>
    public static readonly TimeOnly AllDayTime = new(9, 0);
    /// <summary>Gap between nag alerts.</summary>
    public static readonly TimeSpan NagInterval = TimeSpan.FromMinutes(10);
    /// <summary>An AtTime/Snooze alert fired more than this after its time is flagged overdue.</summary>
    public static readonly TimeSpan OverdueGrace = TimeSpan.FromMinutes(1);

    /// <summary>Occurrence key: due date + due time ("yyyy-MM-ddTHH:mm"); all-day uses 09:00.</summary>
    public static string OccurrenceKey(Reminder r) =>
        DueLocal(r).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public static DateTime DueLocal(Reminder r) =>
        r.DueDate.ToDateTime(r.DueTime ?? AllDayTime);

    /// <summary>Convenience overload: UTC "now" derived from nowLocal via the system time zone.</summary>
    public static IReadOnlyList<PlannedAlert> GetDue(Reminder r, IReadOnlyList<NotificationLogEntry> log,
        DateTime nowLocal, DateTime? snoozedUntilLocal = null) =>
        GetDue(r, log, nowLocal, nowLocal.ToUniversalTime(), snoozedUntilLocal);

    /// <summary>Alerts to fire now: empty or exactly one.</summary>
    public static IReadOnlyList<PlannedAlert> GetDue(Reminder r, IReadOnlyList<NotificationLogEntry> log,
        DateTime nowLocal, DateTime nowUtc, DateTime? snoozedUntilLocal = null)
    {
        var none = Array.Empty<PlannedAlert>();
        if (r.IsCompleted) return none;

        var occ = OccurrenceKey(r);
        var due = DueLocal(r);
        var mine = log.Where(e => e.ReminderId == r.Id && e.Occurrence == occ)
                      .Select(e => (e.Kind, Local: nowLocal - (nowUtc - e.FiredAtUtc)))
                      .ToList();

        if (mine.Any(e => e.Kind == NotificationKind.Dismissed)) return none;

        PlannedAlert[] One(NotificationKind k, bool overdue) => [new PlannedAlert(k, occ, overdue)];
        bool Has(NotificationKind k) => mine.Any(e => e.Kind == k);

        // Snooze: silent until the time, then one Snooze alert.
        if (snoozedUntilLocal is { } snooze)
        {
            if (nowLocal < snooze) return none;
            if (!mine.Any(e => e.Kind == NotificationKind.Snooze && e.Local >= snooze))
                return One(NotificationKind.Snooze, nowLocal - snooze > OverdueGrace);
        }

        if (nowLocal < due)
        {
            if (!r.IsImportant) return none;
            // A pre-alert is due only while its window is open. A Snooze alert fired inside
            // the window counts as that pre-alert (avoids two alerts back to back).
            bool SnoozedSince(DateTime t) => mine.Any(e => e.Kind == NotificationKind.Snooze && e.Local >= t);
            var hourStart = due.AddHours(-1);
            var dayStart = due.AddHours(-24);
            if (nowLocal >= hourStart)
                return Has(NotificationKind.Pre1Hour) || SnoozedSince(hourStart) ? none : One(NotificationKind.Pre1Hour, false);
            if (nowLocal >= dayStart)
                return Has(NotificationKind.Pre1Day) || Has(NotificationKind.Pre1Hour) || SnoozedSince(dayStart)
                    ? none : One(NotificationKind.Pre1Day, false);
            return none;
        }

        // Past due. A Snooze alert fired at/after due also counts as the at-time alert.
        bool atTimeDone = Has(NotificationKind.AtTime) ||
                          mine.Any(e => e.Kind == NotificationKind.Snooze && e.Local >= due);
        if (!atTimeDone)
            return One(NotificationKind.AtTime, nowLocal - due > OverdueGrace);

        if (!r.IsImportant) return none;
        var last = mine.Where(e => e.Kind is NotificationKind.AtTime or NotificationKind.Nag or NotificationKind.Snooze)
                       .Select(e => e.Local).DefaultIfEmpty(DateTime.MinValue).Max();
        return nowLocal - last >= NagInterval ? One(NotificationKind.Nag, true) : none;
    }
}
