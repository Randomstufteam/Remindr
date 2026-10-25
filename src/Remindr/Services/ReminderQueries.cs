using System.Globalization;
using Remindr.Models;

namespace Remindr.Services;

public enum ReminderGroupKind { Overdue, Today, Tomorrow, Date, Completed }

/// <summary>One section of the All Reminders list.</summary>
public sealed record ReminderGroup(ReminderGroupKind Kind, DateOnly? Date, string Title, IReadOnlyList<Reminder> Items);

/// <summary>Pure grouping / sorting / labelling rules for reminders. No WPF, no database; "now" is always passed in.</summary>
public static class ReminderQueries
{
    /// <summary>
    /// The (date, minute) a reminder must be strictly before to count as overdue. Seconds are dropped so this matches the
    /// SQL comparison on the HH:mm text: a 15:00 reminder becomes overdue at 15:01.
    /// </summary>
    public static (DateOnly Today, TimeOnly Time) OverdueCutoff(DateTime now) =>
        (DateOnly.FromDateTime(now), new TimeOnly(now.Hour, now.Minute));

    /// <summary>Incomplete and due before now. All-day reminders are overdue from the day after their date.</summary>
    public static bool IsOverdue(Reminder r, DateTime now)
    {
        if (r.IsCompleted) return false;
        var (today, time) = OverdueCutoff(now);
        if (r.DueDate < today) return true;
        return r.DueDate == today && r.DueTime is { } t && t < time;
    }

    /// <summary>
    /// Overdue (oldest first), Today, Tomorrow, then one group per later date (ascending), then Completed (most recently
    /// completed first). Empty groups are left out. Inside a day: all-day first, then by time, important first, then title.
    /// </summary>
    public static IReadOnlyList<ReminderGroup> Group(IEnumerable<Reminder> reminders, DateTime now, CultureInfo? culture = null)
    {
        culture ??= Loc.Culture;
        var today = DateOnly.FromDateTime(now);
        var tomorrow = today.AddDays(1);
        var list = reminders.ToList();
        var groups = new List<ReminderGroup>();

        var overdue = list.Where(r => IsOverdue(r, now)).OrderBy(r => r.DueDate).ThenBy(Slot).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (overdue.Count > 0) groups.Add(new ReminderGroup(ReminderGroupKind.Overdue, null, Loc.Get("Group_Overdue"), overdue));

        var open = list.Where(r => !r.IsCompleted && !IsOverdue(r, now));
        foreach (var day in open.GroupBy(r => r.DueDate).OrderBy(g => g.Key))
        {
            var kind = day.Key == today ? ReminderGroupKind.Today
                     : day.Key == tomorrow ? ReminderGroupKind.Tomorrow
                     : ReminderGroupKind.Date;
            var title = kind == ReminderGroupKind.Today ? Loc.Get("Group_Today")
                      : kind == ReminderGroupKind.Tomorrow ? Loc.Get("Group_Tomorrow")
                      : DateHeading(day.Key, today, culture);
            groups.Add(new ReminderGroup(kind, day.Key, title, SortWithinDay(day)));
        }

        var done = list.Where(r => r.IsCompleted)
            .OrderByDescending(r => r.CompletedAt ?? "", StringComparer.Ordinal)
            .ThenByDescending(r => r.DueDate).ToList();
        if (done.Count > 0) groups.Add(new ReminderGroup(ReminderGroupKind.Completed, null, Loc.Get("Group_Completed"), done));

        return groups;
    }

    /// <summary>One day's reminders: all-day first, then by time, important first, then title.</summary>
    public static IReadOnlyList<Reminder> SortWithinDay(IEnumerable<Reminder> items) =>
        items.OrderBy(Slot).ThenByDescending(r => r.IsImportant)
             .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    // all-day (no time) sorts before any timed reminder
    private static TimeSpan Slot(Reminder r) => r.DueTime is { } t ? t.ToTimeSpan() : TimeSpan.FromHours(-1);

    /// <summary>"Tuesday, Oct 28"; the year is added when it is not the current year.</summary>
    public static string DateHeading(DateOnly date, DateOnly today, CultureInfo? culture = null)
    {
        culture ??= Loc.Culture;
        return date.ToDateTime(TimeOnly.MinValue).ToString(Loc.Get(date.Year == today.Year ? "Fmt_WeekdayMonthDay" : "Fmt_WeekdayMonthDayYear"), culture);
    }

    /// <summary>Full weekday name for the date, e.g. "Tuesday".</summary>
    public static string DayOfWeekLabel(DateOnly date, CultureInfo? culture = null) =>
        (culture ?? Loc.Culture).DateTimeFormat.GetDayName(date.DayOfWeek);

    /// <summary>"All day" for no time, otherwise the culture's short time ("3:00 PM").</summary>
    public static string TimeLabel(TimeOnly? time, CultureInfo? culture = null) =>
        time is { } t ? t.ToString("t", culture ?? Loc.Culture) : Loc.Get("Time_AllDay");

    /// <summary>
    /// Card caption: "3:00 PM" / "All day"; with <paramref name="showDate"/> the date comes first ("Tue, Oct 28, 3:00 PM",
    /// with the year when it is not the current one).
    /// </summary>
    public static string WhenText(Reminder r, DateTime now, bool showDate, CultureInfo? culture = null)
    {
        culture ??= Loc.Culture;
        var time = TimeLabel(r.DueTime, culture);
        if (!showDate) return time;
        var date = r.DueDate.ToDateTime(TimeOnly.MinValue).ToString(Loc.Get(r.DueDate.Year == now.Year ? "Fmt_ShortWeekdayMonthDay" : "Fmt_ShortWeekdayMonthDayYear"), culture);
        return Loc.Format("Card_DateAndTime", date, time);
    }

    /// <summary>Default slot for a new reminder: the next full hour today; tomorrow 9:00 if that would pass midnight. A given date gets 9:00.</summary>
    public static (DateOnly Date, TimeOnly Time) DefaultNewSlot(DateTime now, DateOnly? date = null)
    {
        if (date is { } d) return (d, new TimeOnly(9, 0));
        var next = now.Date.AddHours(now.Hour + 1);
        return next.Date == now.Date
            ? (DateOnly.FromDateTime(next), TimeOnly.FromDateTime(next))
            : (DateOnly.FromDateTime(now).AddDays(1), new TimeOnly(9, 0));
    }

    /// <summary>Every 15 minutes of the day as culture short-time text, for the time picker's drop-down.</summary>
    public static IReadOnlyList<string> TimeSuggestions(CultureInfo? culture = null)
    {
        culture ??= Loc.Culture;
        return Enumerable.Range(0, 96).Select(i => new TimeOnly(i / 4, i % 4 * 15).ToString("t", culture)).ToList();
    }
}

