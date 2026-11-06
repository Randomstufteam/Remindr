using System.Globalization;
using System.Text.RegularExpressions;

namespace Remindr.Services.Assistant;

/// <summary>A day and time worked out from the user's words. <see cref="DayRecognized"/> is false when day words were given but not understood.</summary>
public sealed record ResolvedWhen(DateOnly Date, TimeOnly? Time, bool DayRecognized);

/// <summary>A snooze from the user's words: a moment in time, or "tomorrow" (which moves the date, like the toast's Tomorrow button).</summary>
public sealed record ResolvedSnooze(DateTime? Until, bool Tomorrow);

/// <summary>
/// Turns day and time words ("friday", "next tuesday", "15th", "tonight", "9", "5:30pm") into real dates. The model never does date math:
/// it only copies the words and this class resolves them against "now" (an <see cref="IClock"/> value). UI-free. Weeks start on Monday.
/// Phase 6.3's quick-add parser is meant to reuse this instead of adding a second date parser.
/// </summary>
public static class DayTimeResolver
{
    static readonly string[] WeekdayNames = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    /// <summary>
    /// Rules: a weekday means the next one; today's weekday means today unless the time is given and already past, then next week;
    /// "next friday" is the one in the following week; bare "tonight" 20:00, "morning" 09:00, "afternoon" 14:00, "evening" 18:00 (only when
    /// no time was said); "15th" is the next 15th. No day: today if the time is still ahead, else tomorrow. Nothing at all: today, all-day.
    /// </summary>
    public static ResolvedWhen Resolve(string? dayWords, string? timeWords, DateTime now)
    {
        var day = Clean(dayWords);
        var time = ParseTime(timeWords);
        var today = DateOnly.FromDateTime(now);

        // A part of the day gives a time only when none was said ("tomorrow morning", "tonight").
        var part = Regex.Match(day, @"\b(tonight|morning|afternoon|evening)\b");
        if (time is null && part.Success)
            time = part.Value switch { "tonight" => new TimeOnly(20, 0), "morning" => new TimeOnly(9, 0), "afternoon" => new TimeOnly(14, 0), _ => new TimeOnly(18, 0) };
        var rest = Regex.Replace(day, @"\b(tonight|morning|afternoon|evening|in|at|the)\b", " ").Trim();
        rest = Regex.Replace(rest, @"\s+", " ");

        // The time of day is "still ahead" when none was said, or it is later than now.
        var ahead = time is null || time.Value > TimeOnly.FromDateTime(now);

        if (rest.Length == 0) return day.Contains("tonight") ? new(today, time, true) : Fallback(today, time, now, recognized: true);
        return DateFor(rest, today, ahead) is { } date ? new(date, time, true) : Fallback(today, time, now, recognized: false);
    }

    /// <summary>No (usable) day said: today if the time is still ahead, else tomorrow; no time either means today, all-day.</summary>
    static ResolvedWhen Fallback(DateOnly today, TimeOnly? time, DateTime now, bool recognized) =>
        new(time is null || time.Value > TimeOnly.FromDateTime(now) ? today : today.AddDays(1), time, recognized);

    /// <summary>
    /// The days a question covers: one day, "this week" (today to Sunday), "next week" (Monday to Sunday), "this weekend". Null when the
    /// words are not a day or range. The time of day never matters for a question, so today's weekday means today.
    /// </summary>
    public static (DateOnly From, DateOnly To)? ResolveRange(string? dayWords, DateTime now)
    {
        var day = Clean(dayWords);
        var today = DateOnly.FromDateTime(now);
        if (day.Length == 0) return null;
        var toSunday = (7 - (int)today.DayOfWeek) % 7;   // Sunday counts as the end of the week
        if (day == "this week") return (today, today.AddDays(toSunday));
        if (day == "next week") { var mon = today.AddDays(toSunday + 1); return (mon, mon.AddDays(6)); }
        if (day == "this weekend")
        {
            var sat = today.DayOfWeek == DayOfWeek.Sunday ? today.AddDays(-1) : today.AddDays((6 - (int)today.DayOfWeek) % 7);
            return (sat < today ? today : sat, sat.AddDays(1));
        }
        var rest = Regex.Replace(day, @"\b(tonight|morning|afternoon|evening|in|at|the)\b", " ").Trim();
        rest = Regex.Replace(rest, @"\s+", " ");
        if (day.Contains("tonight") && rest.Length == 0) return (today, today);
        return DateFor(rest, today, true) is { } d ? (d, d) : null;
    }

    static string Clean(string? words) =>
        Regex.Replace(Regex.Replace((words ?? "").ToLowerInvariant().Replace("'", ""), @"\s+", " ").Trim(), @"^(?:on )?(?:the )?", "");

    /// <summary>The date a day phrase means, or null when it is not a day phrase. <paramref name="ahead"/>: whether today still counts for a weekday or "15th".</summary>
    static DateOnly? DateFor(string day, DateOnly today, bool ahead)
    {
        switch (day)
        {
            case "today": return today;
            case "tomorrow": return today.AddDays(1);
            case "next week": return today.AddDays(7);
            case "this week": return today;
            case "next month": return today.AddMonths(1);
            case "this weekend":
                return today.DayOfWeek == DayOfWeek.Sunday ? today : today.AddDays((6 - (int)today.DayOfWeek) % 7);
        }
        var wd = Regex.Match(day, @"^(?:(?<which>next|this)\s+)?(?<name>sunday|monday|tuesday|wednesday|thursday|friday|saturday)$");
        if (wd.Success)
        {
            var target = Array.IndexOf(WeekdayNames, wd.Groups["name"].Value);
            var ahead0 = (target - (int)today.DayOfWeek + 7) % 7;
            if (wd.Groups["which"].Value == "next")
            {
                // The occurrence after today; when that is still this week, the one in the following week.
                var d = today.AddDays(ahead0 == 0 ? 7 : ahead0);
                return WeekStart(d) == WeekStart(today) ? d.AddDays(7) : d;
            }
            return today.AddDays(ahead0 == 0 && !ahead ? 7 : ahead0);
        }
        var nth = Regex.Match(day, @"^(?<n>\d{1,2})(?:st|nd|rd|th)$");
        if (nth.Success && int.Parse(nth.Groups["n"].Value) is var n and >= 1 and <= 31)
        {
            // The next month (counting this one) that has such a day and where it is not already behind us.
            for (var i = 0; i < 13; i++)
            {
                var first = new DateOnly(today.Year, today.Month, 1).AddMonths(i);
                if (n > DateTime.DaysInMonth(first.Year, first.Month)) continue;
                var d = new DateOnly(first.Year, first.Month, n);
                if (d > today || (d == today && ahead)) return d;
            }
        }
        return null;
    }

    static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>"9am", "9:00 am", "17:00", "at 6", "8", "noon", "midnight" to a time. A bare hour 1 to 8 means the afternoon or evening.</summary>
    public static TimeOnly? ParseTime(string? words)
    {
        if (string.IsNullOrWhiteSpace(words)) return null;
        var s = words.ToLowerInvariant();
        if (Regex.IsMatch(s, @"\b(noon|midday)\b")) return new TimeOnly(12, 0);
        if (Regex.IsMatch(s, @"\bmidnight\b")) return new TimeOnly(0, 0);
        var m = Regex.Match(s, @"(\d{1,2})(?::(\d{2}))?\s*(am|pm|a\.m\.|p\.m\.)?");
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var mer = m.Groups[3].Value;
        if (mer.StartsWith('p') && h < 12) h += 12;
        else if (mer.StartsWith('a') && h == 12) h = 0;
        else if (mer.Length == 0 && h is >= 1 and <= 8) h += 12;
        return h > 23 || min > 59 ? null : new TimeOnly(h, min);
    }

    /// <summary>"5 minutes", "an hour", "20 min", "2 hours", "half an hour" or "tomorrow". Null when it is none of those (the user is asked how long).</summary>
    public static ResolvedSnooze? ParseSnooze(string? words, DateTime now)
    {
        var s = Regex.Replace((words ?? "").ToLowerInvariant(), @"\b(for|in|until|till|to|about|another)\b", " ").Trim();
        if (s == "tomorrow") return new(null, true);
        TimeSpan? span = null;
        if (Regex.IsMatch(s, @"\bhalf an? hour\b")) span = TimeSpan.FromMinutes(30);
        else if (Regex.Match(s, @"^(?<n>\d{1,4}|an?|one)\s*(?<u>minutes?|mins?|hours?|hrs?|h|m)$") is { Success: true } m)
        {
            var n = m.Groups["n"].Value is "a" or "an" or "one" ? 1 : int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            span = m.Groups["u"].Value[0] == 'h' ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
        }
        if (span is not { } sp || sp <= TimeSpan.Zero || sp > TimeSpan.FromDays(1)) return null;
        return new(ToastActionHandler.SnoozeUntil(sp, now), false);
    }
}
