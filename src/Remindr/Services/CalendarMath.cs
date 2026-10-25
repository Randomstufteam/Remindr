using System.Globalization;
using Remindr.Models;

namespace Remindr.Services;

public enum CalendarViewMode { Month, Week, Agenda }

/// <summary>An inclusive span of dates.</summary>
public sealed record DateRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
    public bool Contains(DateOnly d) => d >= From && d <= To;
}

/// <summary>What a month cell shows for one day: up to N category dots (null = uncategorized), a "+" flag and the important flag.</summary>
public sealed record DaySummary(IReadOnlyList<string?> DotColors, bool HasMoreDots, bool HasImportant, int Count);

/// <summary>One date and its reminders (sorted: all-day first, then by time).</summary>
public sealed record DayItems(DateOnly Date, IReadOnlyList<Reminder> Items);

/// <summary>Pure date math for the Calendar page (month grid, week, agenda, dots). No WPF, no database.</summary>
public static class CalendarMath
{
    public const int MonthGridDays = 42;   // 6 rows x 7 columns
    public const int AgendaDays = 30;
    public const int MaxDots = 3;

    /// <summary>The first day of the week for the culture (Sunday for en-US, Monday for most of Europe).</summary>
    public static DayOfWeek FirstDayOfWeek(CultureInfo? culture = null) =>
        (culture ?? Loc.Culture).DateTimeFormat.FirstDayOfWeek;

    /// <summary>The most recent <paramref name="firstDay"/> on or before <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date, DayOfWeek firstDay) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)firstDay + 7) % 7));

    /// <summary>The 42-day grid (6 weeks) that shows the month containing <paramref name="anyDayInMonth"/>.</summary>
    public static DateRange MonthGridRange(DateOnly anyDayInMonth, DayOfWeek firstDay)
    {
        var start = WeekStart(new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1), firstDay);
        return new DateRange(start, start.AddDays(MonthGridDays - 1));
    }

    /// <summary>The 42 dates of the month grid in display order (row by row).</summary>
    public static IReadOnlyList<DateOnly> MonthGridDates(DateOnly anyDayInMonth, DayOfWeek firstDay)
    {
        var start = MonthGridRange(anyDayInMonth, firstDay).From;
        return Enumerable.Range(0, MonthGridDays).Select(start.AddDays).ToList();
    }

    /// <summary>The 7 days of the week containing <paramref name="anchor"/>.</summary>
    public static DateRange WeekRange(DateOnly anchor, DayOfWeek firstDay)
    {
        var start = WeekStart(anchor, firstDay);
        return new DateRange(start, start.AddDays(6));
    }

    /// <summary>The anchor day and the following days (30 days in total by default).</summary>
    public static DateRange AgendaRange(DateOnly anchor, int days = AgendaDays) =>
        new(anchor, anchor.AddDays(Math.Max(1, days) - 1));

    /// <summary>The dates that must be loaded for the view.</summary>
    public static DateRange VisibleRange(CalendarViewMode mode, DateOnly anchor, DayOfWeek firstDay) => mode switch
    {
        CalendarViewMode.Month => MonthGridRange(anchor, firstDay),
        CalendarViewMode.Week => WeekRange(anchor, firstDay),
        _ => AgendaRange(anchor)
    };

    /// <summary>Anchor after pressing prev (direction -1) or next (+1): a month (day clamped to the month length), 7 days or 30 days.</summary>
    public static DateOnly Navigate(CalendarViewMode mode, DateOnly anchor, int direction) => mode switch
    {
        CalendarViewMode.Month => anchor.AddMonths(direction),
        CalendarViewMode.Week => anchor.AddDays(7 * direction),
        _ => anchor.AddDays(AgendaDays * direction)
    };

    public static bool IsSameMonth(DateOnly a, DateOnly b) => a.Year == b.Year && a.Month == b.Month;

    /// <summary>Arrow-key movement in the month grid: <paramref name="dx"/> days left/right, <paramref name="dy"/> weeks up/down.</summary>
    public static DateOnly MoveSelection(DateOnly selected, int dx, int dy) => selected.AddDays(dx + 7 * dy);

    /// <summary>
    /// "October 2026", "Oct 19 – 25, 2026", "Oct 26 – Nov 1, 2026", "Dec 28, 2026 – Jan 3, 2027" or "Next 30 days from Oct 24" in
    /// English. The date patterns and sentence shapes are resources (Fmt_*, Calendar_*), so each language can order them its own way.
    /// </summary>
    public static string Title(CalendarViewMode mode, DateOnly anchor, DayOfWeek firstDay, CultureInfo? culture = null)
    {
        culture ??= Loc.Culture;
        switch (mode)
        {
            case CalendarViewMode.Month:
                return anchor.ToString(Loc.Get("Fmt_MonthYear"), culture);
            case CalendarViewMode.Week:
                var r = WeekRange(anchor, firstDay);
                if (r.From.Year != r.To.Year)
                    return Loc.Format("Calendar_WeekRangeCrossYear",
                        r.From.ToString(Loc.Get("Fmt_MonthDayYear"), culture), r.To.ToString(Loc.Get("Fmt_MonthDayYear"), culture));
                // Each end gets its own pattern, so a language can put the year, month and day where it wants them
                // ("19.–25. Okt. 2026", "2026年10月19日～25日", "2026. okt. 19–25.").
                if (r.From.Month != r.To.Month)
                    return Loc.Format("Calendar_WeekRangeCrossMonth",
                        r.From.ToString(Loc.Get("Fmt_WeekRangeCrossMonthStart"), culture), r.To.ToString(Loc.Get("Fmt_WeekRangeCrossMonthEnd"), culture));
                return Loc.Format("Calendar_WeekRangeSameMonth",
                    r.From.ToString(Loc.Get("Fmt_WeekRangeSameMonthStart"), culture), r.To.ToString(Loc.Get("Fmt_WeekRangeSameMonthEnd"), culture));
            default:
                return Loc.Format("Calendar_AgendaTitle", AgendaDays, anchor.ToString(Loc.Get("Fmt_MonthDay"), culture));
        }
    }

    /// <summary>Abbreviated weekday names starting at <paramref name="firstDay"/> ("Sun", "Mon", ...).</summary>
    public static IReadOnlyList<string> WeekdayLabels(DayOfWeek firstDay, CultureInfo? culture = null)
    {
        var f = (culture ?? Loc.Culture).DateTimeFormat;
        return Enumerable.Range(0, 7).Select(i => f.GetAbbreviatedDayName((DayOfWeek)(((int)firstDay + i) % 7))).ToList();
    }

    /// <summary>Reminders by due date; each day sorted all-day first, then time, important first, title.</summary>
    public static IReadOnlyDictionary<DateOnly, IReadOnlyList<Reminder>> GroupByDay(IEnumerable<Reminder> reminders) =>
        reminders.GroupBy(r => r.DueDate)
                 .ToDictionary(g => g.Key, g => ReminderQueries.SortWithinDay(g));

    /// <summary>The reminders due on <paramref name="day"/> (sorted), or empty.</summary>
    public static IReadOnlyList<Reminder> ForDay(IEnumerable<Reminder> reminders, DateOnly day) =>
        ReminderQueries.SortWithinDay(reminders.Where(r => r.DueDate == day));

    /// <summary>Days that have reminders inside <paramref name="range"/>, in date order. Empty days are skipped.</summary>
    public static IReadOnlyList<DayItems> Agenda(IEnumerable<Reminder> reminders, DateRange range) =>
        GroupByDay(reminders.Where(r => range.Contains(r.DueDate)))
            .OrderBy(kv => kv.Key)
            .Select(kv => new DayItems(kv.Key, kv.Value))
            .ToList();

    /// <summary>
    /// Dots for one day: one per distinct category colour in item order (uncategorized = null = neutral), capped at
    /// <paramref name="maxDots"/> with HasMoreDots set when more were left out. HasImportant if any reminder that day is important.
    /// </summary>
    public static DaySummary SummarizeDay(IEnumerable<Reminder> dayItems, IReadOnlyDictionary<int, Category> categories, int maxDots = MaxDots)
    {
        var colors = new List<string?>();
        var important = false;
        var count = 0;
        foreach (var r in dayItems)
        {
            count++;
            important |= r.IsImportant;
            string? color = r.CategoryId is { } id && categories.TryGetValue(id, out var c) ? c.Color : null;
            if (!colors.Any(x => string.Equals(x, color, StringComparison.OrdinalIgnoreCase))) colors.Add(color);
        }
        var cap = Math.Max(0, maxDots);
        return new DaySummary(colors.Take(cap).ToList(), colors.Count > cap, important, count);
    }
}
