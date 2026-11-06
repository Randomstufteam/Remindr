using System.Text.RegularExpressions;
using Remindr.Models;

namespace Remindr.Services.Assistant;

/// <summary>
/// Fuzzy match of the words the user used ("dentist", "grocery shopping") against existing reminder titles. Compares lower-cased
/// words, drops stop words and counts a shared prefix as a match (stretch / stretching, cat / cats). The score of a reminder is the share
/// of the user's words found in its title; every reminder with the best score is a candidate, nearest due date first. UI-free.
/// </summary>
public static class ReminderMatcher
{
    static readonly HashSet<string> Stop = new() { "the", "my", "a", "an", "one", "reminder", "reminders", "to", "for", "of", "and", "with", "about", "on", "in", "at", "i", "me" };

    /// <summary>The words of a phrase that carry meaning.</summary>
    public static List<string> Words(string? text) =>
        Regex.Split((text ?? "").ToLowerInvariant().Replace("'", "").Replace("’", ""), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0 && !Stop.Contains(w)).ToList();

    static bool Same(string a, string b) => a == b || (Math.Min(a.Length, b.Length) >= 3 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)));

    /// <summary>Share (0 to 1) of the query words that appear in the title.</summary>
    public static double Score(IReadOnlyList<string> query, string title)
    {
        if (query.Count == 0) return 0;
        var t = Words(title);
        return query.Count(q => t.Any(w => Same(q, w))) / (double)query.Count;
    }

    /// <summary>Every reminder scoring above zero, best first, then nearest due date first.</summary>
    public static List<(Reminder Reminder, double Score)> Rank(string? words, IEnumerable<Reminder> reminders)
    {
        var q = Words(words);
        return reminders.Select(r => (Reminder: r, Score: Score(q, r.Title))).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Reminder.DueDate).ThenBy(x => x.Reminder.DueTime ?? TimeOnly.MinValue).ToList();
    }

    /// <summary>
    /// The reminders that best fit <paramref name="words"/> among the <paramref name="open"/> ones. Empty: nothing fits (at least half of the
    /// user's words must be found). One: use it. Several: they fit equally well, so the user is asked which (nearest due date listed first).
    /// </summary>
    public static IReadOnlyList<Reminder> Match(string? words, IEnumerable<Reminder> open)
    {
        var ranked = Rank(words, open.Where(r => !r.IsCompleted));
        if (ranked.Count == 0 || ranked[0].Score < 0.5) return Array.Empty<Reminder>();
        var best = ranked[0].Score;
        return ranked.Where(x => Math.Abs(x.Score - best) < 1e-9).Select(x => x.Reminder).ToList();
    }
}
