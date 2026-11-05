using Remindr.Models;

namespace Remindr.Services;

/// <summary>The Today page's four lists. Today and Next 7 days keep completed items (the page can hide them); the counts below ignore them.</summary>
public sealed record TodayBuckets(IReadOnlyList<Reminder> Overdue, IReadOnlyList<Reminder> Today, IReadOnlyList<Reminder> Next7,
    IReadOnlyList<Reminder> Important);

/// <summary>Sorts reminders into the Today page's buckets and writes the one-line summary. No UI, so the page and the daily toast share it and tests need neither.</summary>
public static class SummaryService
{
    /// <summary>"Next 7 days" and "important this week" both look this many days past today.</summary>
    public const int WindowDays = 7;

    public static TodayBuckets Buckets(IEnumerable<Reminder> all, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var end = today.AddDays(WindowDays);
        var list = all.ToList();
        static IReadOnlyList<Reminder> Sorted(IEnumerable<Reminder> rs) =>
            rs.OrderBy(r => r.DueDate).ThenBy(r => r.DueTime ?? TimeOnly.MinValue)
              .ThenByDescending(r => r.IsImportant).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new TodayBuckets(
            Sorted(list.Where(r => ReminderQueries.IsOverdue(r, now))),
            Sorted(list.Where(r => r.DueDate == today && !ReminderQueries.IsOverdue(r, now))),
            Sorted(list.Where(r => r.DueDate > today && r.DueDate <= end)),
            // Open important reminders in the same window; they also sit in Today / Next 7 days.
            Sorted(list.Where(r => r.IsImportant && !r.IsCompleted && !ReminderQueries.IsOverdue(r, now) && r.DueDate >= today && r.DueDate <= end)));
    }

    /// <summary>"3 due today, 1 overdue, 2 important this week"; only the non-zero parts, or the "nothing due" text when there are none.</summary>
    public static string Sentence(TodayBuckets b)
    {
        var parts = new List<string>();
        void Add(string key, int n) { if (n > 0) parts.Add(Loc.Plural(key, n)); }
        Add("Summary_Due", b.Today.Count(r => !r.IsCompleted));
        Add("Summary_Overdue", b.Overdue.Count);
        Add("Summary_Important", b.Important.Count);
        return parts.Count == 0 ? Loc.Get("Today_Empty_Title") : parts.Aggregate((a, p) => Loc.Format("Summary_Join", a, p));
    }
}
