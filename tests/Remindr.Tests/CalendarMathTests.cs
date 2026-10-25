using System.Globalization;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.Tests;

public class CalendarMathTests
{
    private static readonly CultureInfo En = new("en-US");
    private static readonly CultureInfo Gb = new("en-GB");

    // Saturday 2026-10-24
    private static readonly DateOnly Oct24 = new(2026, 10, 24);

    private static readonly Dictionary<int, Category> Cats = new()
    {
        [1] = new Category { Id = 1, Name = "Work", Color = "#EF4444" },
        [2] = new Category { Id = 2, Name = "Home", Color = "#3B82F6" },
        [3] = new Category { Id = 3, Name = "Dupe", Color = "#ef4444" },   // same colour as 1, different case
        [4] = new Category { Id = 4, Name = "Four", Color = "#10B981" },
        [5] = new Category { Id = 5, Name = "Five", Color = "#F59E0B" },
    };

    private static Reminder R(string title, string date, string? time = null, bool imp = false, bool done = false, int? cat = null)
    {
        var r = ReminderEnv.R(title, date, time, done);
        r.IsImportant = imp;
        r.CategoryId = cat;
        return r;
    }

    private static Reminder Item(int? cat = null, bool imp = false, bool done = false) =>
        R("item", "2026-10-24", null, imp, done, cat);

    // ---------- FirstDayOfWeek / WeekStart ----------

    [Fact]
    public void FirstDayOfWeek_FollowsCulture()
    {
        Assert.Equal(DayOfWeek.Sunday, CalendarMath.FirstDayOfWeek(En));
        Assert.Equal(DayOfWeek.Monday, CalendarMath.FirstDayOfWeek(Gb));
    }

    [Fact]
    public void WeekStart_SundayStart()
    {
        Assert.Equal(new DateOnly(2026, 10, 18), CalendarMath.WeekStart(Oct24, DayOfWeek.Sunday));
        Assert.Equal(new DateOnly(2026, 10, 18), CalendarMath.WeekStart(new DateOnly(2026, 10, 18), DayOfWeek.Sunday)); // already the start
        Assert.Equal(new DateOnly(2026, 10, 11), CalendarMath.WeekStart(new DateOnly(2026, 10, 17), DayOfWeek.Sunday));
    }

    [Fact]
    public void WeekStart_MondayStart()
    {
        Assert.Equal(new DateOnly(2026, 10, 19), CalendarMath.WeekStart(Oct24, DayOfWeek.Monday));
        Assert.Equal(new DateOnly(2026, 10, 19), CalendarMath.WeekStart(new DateOnly(2026, 10, 19), DayOfWeek.Monday)); // already the start
        Assert.Equal(new DateOnly(2026, 10, 12), CalendarMath.WeekStart(new DateOnly(2026, 10, 18), DayOfWeek.Monday)); // Sunday belongs to the previous week
    }

    // ---------- Month grid ----------

    [Fact]
    public void MonthGridRange_Oct2026_SundayStart_Is42DaysFromSep27()
    {
        var range = CalendarMath.MonthGridRange(Oct24, DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 9, 27), range.From);
        Assert.Equal(new DateOnly(2026, 11, 7), range.To);
        Assert.Equal(42, range.Days);
    }

    [Fact]
    public void MonthGridRange_UsesTheMonthNotTheAnchorDay()
    {
        Assert.Equal(CalendarMath.MonthGridRange(new DateOnly(2026, 10, 1), DayOfWeek.Sunday),
                     CalendarMath.MonthGridRange(new DateOnly(2026, 10, 31), DayOfWeek.Sunday));
    }

    [Fact]
    public void MonthGridDates_AlwaysFortyTwo_StartsOnWeekStart_ContainsFirstAndLastDay()
    {
        foreach (var firstDay in new[] { DayOfWeek.Sunday, DayOfWeek.Monday })
        for (var month = 1; month <= 12; month++)
        {
            var first = new DateOnly(2026, month, 1);
            var last = new DateOnly(2026, month, DateTime.DaysInMonth(2026, month));
            var dates = CalendarMath.MonthGridDates(first, firstDay);

            Assert.Equal(42, dates.Count);
            Assert.Equal(firstDay, dates[0].DayOfWeek);
            Assert.Contains(first, dates);
            Assert.Contains(last, dates);
            for (var i = 1; i < dates.Count; i++)
                Assert.Equal(dates[i - 1].AddDays(1), dates[i]); // consecutive, display order
        }
    }

    [Fact]
    public void MonthGridDates_Feb2026_SundayStart_MonthStartsOnFirstWeekday()
    {
        // Feb 1 2026 is a Sunday, so the grid begins exactly on the 1st with no leading days.
        var dates = CalendarMath.MonthGridDates(new DateOnly(2026, 2, 1), DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 2, 1), dates[0]);
        Assert.Equal(new DateOnly(2026, 2, 28), dates[27]);
        Assert.Equal(new DateOnly(2026, 3, 14), dates[41]);
    }

    [Fact]
    public void MonthGridDates_Feb2026_MondayStart_HasLeadingDaysFromJanuary()
    {
        var dates = CalendarMath.MonthGridDates(new DateOnly(2026, 2, 1), DayOfWeek.Monday);
        Assert.Equal(new DateOnly(2026, 1, 26), dates[0]);
        Assert.Equal(new DateOnly(2026, 3, 8), dates[41]);
    }

    [Fact]
    public void MonthGridDates_Jun2026_MondayStart_MonthStartsOnFirstWeekday()
    {
        // Jun 1 2026 is a Monday.
        var dates = CalendarMath.MonthGridDates(new DateOnly(2026, 6, 15), DayOfWeek.Monday);
        Assert.Equal(new DateOnly(2026, 6, 1), dates[0]);
        Assert.Equal(new DateOnly(2026, 7, 12), dates[41]);
    }

    [Fact]
    public void MonthGridDates_Dec2026_SundayStart_SpansYearEnd()
    {
        var dates = CalendarMath.MonthGridDates(new DateOnly(2026, 12, 1), DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 11, 29), dates[0]);
        Assert.Equal(new DateOnly(2027, 1, 9), dates[41]);
        Assert.Contains(new DateOnly(2026, 12, 31), dates);
    }

    // ---------- Week / agenda ranges ----------

    [Fact]
    public void WeekRange_SundayAndMondayStart()
    {
        var sun = CalendarMath.WeekRange(Oct24, DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 10, 18), sun.From);
        Assert.Equal(new DateOnly(2026, 10, 24), sun.To);
        Assert.Equal(7, sun.Days);

        var mon = CalendarMath.WeekRange(Oct24, DayOfWeek.Monday);
        Assert.Equal(new DateOnly(2026, 10, 19), mon.From);
        Assert.Equal(new DateOnly(2026, 10, 25), mon.To);
    }

    [Fact]
    public void AgendaRange_IsThirtyDaysInclusive()
    {
        var range = CalendarMath.AgendaRange(Oct24);
        Assert.Equal(new DateOnly(2026, 10, 24), range.From);
        Assert.Equal(new DateOnly(2026, 11, 22), range.To); // Oct 24 + 29 days = last of the 30 inclusive days
        Assert.Equal(30, range.Days);
        Assert.True(range.Contains(new DateOnly(2026, 11, 22)));
        Assert.False(range.Contains(new DateOnly(2026, 11, 23)));
        Assert.False(range.Contains(new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void AgendaRange_ExplicitDayCount_AndClampsToAtLeastOne()
    {
        Assert.Equal(1, CalendarMath.AgendaRange(Oct24, 1).Days);
        Assert.Equal(new DateOnly(2026, 10, 24), CalendarMath.AgendaRange(Oct24, 1).To);
        Assert.Equal(1, CalendarMath.AgendaRange(Oct24, 0).Days); // Math.Max(1, days)
    }

    [Fact]
    public void VisibleRange_PerMode()
    {
        Assert.Equal(CalendarMath.MonthGridRange(Oct24, DayOfWeek.Sunday),
                     CalendarMath.VisibleRange(CalendarViewMode.Month, Oct24, DayOfWeek.Sunday));
        Assert.Equal(CalendarMath.WeekRange(Oct24, DayOfWeek.Sunday),
                     CalendarMath.VisibleRange(CalendarViewMode.Week, Oct24, DayOfWeek.Sunday));
        Assert.Equal(CalendarMath.AgendaRange(Oct24),
                     CalendarMath.VisibleRange(CalendarViewMode.Agenda, Oct24, DayOfWeek.Sunday));

        var month = CalendarMath.VisibleRange(CalendarViewMode.Month, Oct24, DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 9, 27), month.From);
        Assert.Equal(new DateOnly(2026, 11, 7), month.To);
        Assert.Equal(new DateOnly(2026, 10, 18), CalendarMath.VisibleRange(CalendarViewMode.Week, Oct24, DayOfWeek.Sunday).From);
        Assert.Equal(new DateOnly(2026, 11, 22), CalendarMath.VisibleRange(CalendarViewMode.Agenda, Oct24, DayOfWeek.Sunday).To);
    }

    [Fact]
    public void DateRange_Days_IsInclusive()
    {
        Assert.Equal(1, new DateRange(Oct24, Oct24).Days);
        Assert.Equal(2, new DateRange(Oct24, new DateOnly(2026, 10, 25)).Days);
    }

    // ---------- Navigate / MoveSelection ----------

    [Fact]
    public void Navigate_Month_ClampsDayToMonthLength()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), CalendarMath.Navigate(CalendarViewMode.Month, new DateOnly(2026, 1, 31), 1));
        Assert.Equal(new DateOnly(2028, 2, 29), CalendarMath.Navigate(CalendarViewMode.Month, new DateOnly(2028, 1, 31), 1));
        Assert.Equal(new DateOnly(2026, 2, 28), CalendarMath.Navigate(CalendarViewMode.Month, new DateOnly(2026, 3, 31), -1));
    }

    [Fact]
    public void Navigate_Month_CrossesYearBoundary()
    {
        Assert.Equal(new DateOnly(2027, 1, 15), CalendarMath.Navigate(CalendarViewMode.Month, new DateOnly(2026, 12, 15), 1));
        Assert.Equal(new DateOnly(2026, 1, 15), CalendarMath.Navigate(CalendarViewMode.Month, new DateOnly(2027, 1, 15), -12));
    }

    [Fact]
    public void Navigate_Week_Moves7Days()
    {
        Assert.Equal(new DateOnly(2026, 10, 31), CalendarMath.Navigate(CalendarViewMode.Week, Oct24, 1));
        Assert.Equal(new DateOnly(2026, 10, 17), CalendarMath.Navigate(CalendarViewMode.Week, Oct24, -1));
        Assert.Equal(new DateOnly(2027, 1, 5), CalendarMath.Navigate(CalendarViewMode.Week, new DateOnly(2026, 12, 29), 1));
    }

    [Fact]
    public void Navigate_Agenda_Moves30Days()
    {
        Assert.Equal(new DateOnly(2026, 11, 23), CalendarMath.Navigate(CalendarViewMode.Agenda, Oct24, 1));
        Assert.Equal(new DateOnly(2026, 9, 24), CalendarMath.Navigate(CalendarViewMode.Agenda, Oct24, -1));
    }

    [Fact]
    public void MoveSelection_DxIsDaysDyIsWeeks()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), CalendarMath.MoveSelection(Oct24, 1, 0));
        Assert.Equal(new DateOnly(2026, 10, 23), CalendarMath.MoveSelection(Oct24, -1, 0));
        Assert.Equal(new DateOnly(2026, 10, 31), CalendarMath.MoveSelection(Oct24, 0, 1));
        Assert.Equal(new DateOnly(2026, 10, 16), CalendarMath.MoveSelection(Oct24, -1, -1));
        Assert.Equal(new DateOnly(2026, 10, 30), CalendarMath.MoveSelection(Oct24, -1, 1));
        Assert.Equal(new DateOnly(2026, 11, 1), CalendarMath.MoveSelection(new DateOnly(2026, 10, 31), 1, 0));
    }

    // ---------- Titles / labels ----------

    [Fact]
    public void Title_Month()
    {
        Assert.Equal("October 2026", CalendarMath.Title(CalendarViewMode.Month, Oct24, DayOfWeek.Sunday, En));
    }

    [Fact]
    public void Title_Week_SameMonth()
    {
        Assert.Equal("Oct 19 – 25, 2026", CalendarMath.Title(CalendarViewMode.Week, Oct24, DayOfWeek.Monday, En));
    }

    [Fact]
    public void Title_Week_CrossesMonth()
    {
        Assert.Equal("Oct 26 – Nov 1, 2026", CalendarMath.Title(CalendarViewMode.Week, new DateOnly(2026, 10, 26), DayOfWeek.Monday, En));
    }

    [Fact]
    public void Title_Week_CrossesYear()
    {
        Assert.Equal("Dec 28, 2026 – Jan 3, 2027", CalendarMath.Title(CalendarViewMode.Week, new DateOnly(2026, 12, 28), DayOfWeek.Monday, En));
    }

    [Fact]
    public void Title_Agenda()
    {
        Assert.Equal("Next 30 days from Oct 24", CalendarMath.Title(CalendarViewMode.Agenda, Oct24, DayOfWeek.Sunday, En));
    }

    [Fact]
    public void WeekdayLabels_SundayStart()
    {
        Assert.Equal(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" },
            CalendarMath.WeekdayLabels(DayOfWeek.Sunday, En).ToArray());
    }

    [Fact]
    public void WeekdayLabels_MondayStart()
    {
        Assert.Equal(new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
            CalendarMath.WeekdayLabels(DayOfWeek.Monday, En).ToArray());
    }

    [Fact]
    public void IsSameMonth_ComparesYearAndMonth()
    {
        Assert.True(CalendarMath.IsSameMonth(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        Assert.False(CalendarMath.IsSameMonth(new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1)));
        Assert.False(CalendarMath.IsSameMonth(new DateOnly(2026, 10, 1), new DateOnly(2025, 10, 1)));
    }

    // ---------- Grouping / agenda ----------

    [Fact]
    public void GroupByDay_KeysOnlyDaysWithReminders_SortedWithinDay()
    {
        var groups = CalendarMath.GroupByDay(new[]
        {
            R("timed", "2026-10-24", "15:00"),
            R("allday", "2026-10-24"),
            R("other", "2026-10-26", "09:00"),
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "allday", "timed" }, groups[Oct24].Select(r => r.Title).ToArray());
        Assert.Equal(new[] { "other" }, groups[new DateOnly(2026, 10, 26)].Select(r => r.Title).ToArray());
        Assert.False(groups.ContainsKey(new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public void ForDay_ReturnsOnlyThatDaySorted_OrEmpty()
    {
        var reminders = new[]
        {
            R("late", "2026-10-24", "18:00"),
            R("early", "2026-10-24", "08:00"),
            R("allday", "2026-10-24"),
            R("tomorrow", "2026-10-25", "08:00"),
        };

        Assert.Equal(new[] { "allday", "early", "late" },
            CalendarMath.ForDay(reminders, Oct24).Select(r => r.Title).ToArray());
        Assert.Empty(CalendarMath.ForDay(reminders, new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public void Agenda_IncludesOnlyRangeDays_InDateOrder_SkippingEmptyDays()
    {
        var reminders = new[]
        {
            R("before range", "2026-10-23", "10:00"),
            R("day1 timed", "2026-10-24", "09:00"),
            R("day1 allday", "2026-10-24"),
            R("day3", "2026-10-26", "12:00"),
            R("last day", "2026-11-22"),
            R("after range", "2026-11-23"),
        };

        var agenda = CalendarMath.Agenda(reminders, CalendarMath.AgendaRange(Oct24));

        Assert.Equal(3, agenda.Count);             // Oct 25 and the rest of the empty days are skipped
        Assert.Equal(new DateOnly(2026, 10, 24), agenda[0].Date);
        Assert.Equal(new[] { "day1 allday", "day1 timed" }, agenda[0].Items.Select(r => r.Title).ToArray());
        Assert.Equal(new DateOnly(2026, 10, 26), agenda[1].Date);
        Assert.Equal(new DateOnly(2026, 11, 22), agenda[2].Date);
    }

    [Fact]
    public void Agenda_NoReminders_IsEmpty()
    {
        Assert.Empty(CalendarMath.Agenda(Array.Empty<Reminder>(), CalendarMath.AgendaRange(Oct24)));
    }

    // ---------- SummarizeDay ----------

    [Fact]
    public void SummarizeDay_DistinctColors_CaseInsensitive_InItemOrder()
    {
        var summary = CalendarMath.SummarizeDay(new[]
        {
            Item(cat: 1),   // #EF4444
            Item(cat: 3),   // #ef4444 -> same dot
            Item(cat: 2),   // #3B82F6
            Item(cat: 1),
        }, Cats);

        Assert.Equal(new string?[] { "#EF4444", "#3B82F6" }, summary.DotColors.ToArray());
        Assert.False(summary.HasMoreDots);
        Assert.Equal(4, summary.Count);
    }

    [Fact]
    public void SummarizeDay_UncategorizedAndUnknownCategory_AreOneNeutralDot()
    {
        var summary = CalendarMath.SummarizeDay(new[]
        {
            Item(cat: null),
            Item(cat: 99),          // not in the dictionary
            Item(cat: 1),
        }, Cats);

        Assert.Equal(new string?[] { null, "#EF4444" }, summary.DotColors.ToArray());
        Assert.Equal(3, summary.Count);
    }

    [Fact]
    public void SummarizeDay_CapsDots_AndSetsHasMoreDots()
    {
        var fourColors = new[] { Item(cat: 1), Item(cat: 2), Item(cat: 4), Item(cat: 5) };

        var capped = CalendarMath.SummarizeDay(fourColors, Cats);
        Assert.Equal(new string?[] { "#EF4444", "#3B82F6", "#10B981" }, capped.DotColors.ToArray()); // default MaxDots = 3
        Assert.True(capped.HasMoreDots);
        Assert.Equal(4, capped.Count);

        var threeColors = CalendarMath.SummarizeDay(new[] { Item(cat: 1), Item(cat: 2), Item(cat: 4) }, Cats);
        Assert.Equal(3, threeColors.DotColors.Count);
        Assert.False(threeColors.HasMoreDots);
    }

    [Fact]
    public void SummarizeDay_CustomMaxDots_AndZero()
    {
        var items = new[] { Item(cat: 1), Item(cat: 2) };
        var one = CalendarMath.SummarizeDay(items, Cats, maxDots: 1);
        Assert.Equal(new string?[] { "#EF4444" }, one.DotColors.ToArray());
        Assert.True(one.HasMoreDots);

        var zero = CalendarMath.SummarizeDay(items, Cats, maxDots: 0);
        Assert.Empty(zero.DotColors);
        Assert.True(zero.HasMoreDots);
    }

    [Fact]
    public void SummarizeDay_HasImportant_IncludesCompletedItems()
    {
        Assert.True(CalendarMath.SummarizeDay(new[] { Item(), Item(imp: true) }, Cats).HasImportant);
        Assert.True(CalendarMath.SummarizeDay(new[] { Item(imp: true, done: true) }, Cats).HasImportant);
        Assert.False(CalendarMath.SummarizeDay(new[] { Item(done: true), Item() }, Cats).HasImportant);
    }

    [Fact]
    public void SummarizeDay_Empty()
    {
        var summary = CalendarMath.SummarizeDay(Array.Empty<Reminder>(), Cats);
        Assert.Empty(summary.DotColors);
        Assert.False(summary.HasMoreDots);
        Assert.False(summary.HasImportant);
        Assert.Equal(0, summary.Count);
    }

    // ---------- SortWithinDay (ReminderQueries) ----------

    [Fact]
    public void SortWithinDay_AllDayFirst_ThenByTime()
    {
        var sorted = ReminderQueries.SortWithinDay(new[]
        {
            R("timed 15:00", "2026-10-24", "15:00"),
            R("allday", "2026-10-24"),
            R("timed 09:00", "2026-10-24", "09:00"),
        });

        Assert.Equal(new[] { "allday", "timed 09:00", "timed 15:00" }, sorted.Select(r => r.Title).ToArray());
    }

    [Fact]
    public void SortWithinDay_ImportantFirstWithinSameSlot_ThenTitle()
    {
        var sorted = ReminderQueries.SortWithinDay(new[]
        {
            R("zeta", "2026-10-24"),
            R("alpha important", "2026-10-24", imp: true),
            R("beta", "2026-10-24", "09:00"),
            R("Alpha", "2026-10-24", "09:00"),
        });

        Assert.Equal(new[] { "alpha important", "zeta", "Alpha", "beta" }, sorted.Select(r => r.Title).ToArray());
    }

    // ---------- WhenText (ReminderQueries) ----------

    // Now = Saturday 2026-10-24 10:30 (same year as the reminders below unless stated)
    private static readonly DateTime Now = new(2026, 10, 24, 10, 30, 0);

    [Fact]
    public void WhenText_TimedWithoutDate_ShowsShortTime()
    {
        Assert.Equal("3:00 PM", ReminderQueries.WhenText(R("x", "2026-10-27", "15:00"), Now, showDate: false, En));
        Assert.Equal("9:05 AM", ReminderQueries.WhenText(R("x", "2026-10-27", "09:05"), Now, showDate: false, En));
    }

    [Fact]
    public void WhenText_AllDayWithoutDate_IsAllDay()
    {
        Assert.Equal("All day", ReminderQueries.WhenText(R("x", "2026-10-27"), Now, showDate: false, En));
    }

    [Fact]
    public void WhenText_WithDate_SameYear_DatePrefixed()
    {
        Assert.Equal("Tue, Oct 27, 3:00 PM", ReminderQueries.WhenText(R("x", "2026-10-27", "15:00"), Now, showDate: true, En));
        Assert.Equal("Tue, Oct 27, All day", ReminderQueries.WhenText(R("x", "2026-10-27"), Now, showDate: true, En));
    }

    [Fact]
    public void WhenText_WithDate_OtherYear_IncludesYear()
    {
        Assert.Equal("Tue, Jan 5, 2027, 9:00 AM", ReminderQueries.WhenText(R("x", "2027-01-05", "09:00"), Now, showDate: true, En));
    }
}

/// <summary>Week titles are built from per-language patterns (start and end formatted separately, then joined).</summary>
public class WeekRangeTitleTests
{
    private static string Range(string code, DateOnly from, DateOnly to)
    {
        var res = CultureInfo.GetCultureInfo(code);
        string P(string key, DateOnly d) { var f = Remindr.Services.Loc.Get(key, res); try { return d.ToString(f, res); } catch (FormatException) { throw new FormatException($"{code} {key} = [{f}]"); } }
        if (from.Year != to.Year)
            return string.Format(Remindr.Services.Loc.Get("Calendar_WeekRangeCrossYear", res), P("Fmt_MonthDayYear", from), P("Fmt_MonthDayYear", to));
        if (from.Month != to.Month)
            return string.Format(Remindr.Services.Loc.Get("Calendar_WeekRangeCrossMonth", res), P("Fmt_WeekRangeCrossMonthStart", from), P("Fmt_WeekRangeCrossMonthEnd", to));
        return string.Format(Remindr.Services.Loc.Get("Calendar_WeekRangeSameMonth", res), P("Fmt_WeekRangeSameMonthStart", from), P("Fmt_WeekRangeSameMonthEnd", to));
    }

    private static readonly DateOnly Mon19 = new(2026, 10, 19), Sun25 = new(2026, 10, 25), Mon26 = new(2026, 10, 26), Sun1 = new(2026, 11, 1);

    [Fact]
    public void Japanese_Korean_Chinese_PutTheYearFirstAndShortenTheEnd()
    {
        Assert.Equal("2026年10月19日～25日", Range("ja", Mon19, Sun25));
        Assert.Equal("2026年10月26日～11月1日", Range("ja", Mon26, Sun1));
        Assert.Equal("2026년 10월 19일 ~ 25일", Range("ko", Mon19, Sun25));
        Assert.Equal("2026年10月19日至25日", Range("zh-Hans", Mon19, Sun25));
        Assert.Equal("2026年10月26日至11月1日", Range("zh-Hant", Mon26, Sun1));
    }

    [Fact]
    public void Hungarian_IsYearFirst_WithOneYear()
    {
        var s = Range("hu", Mon19, Sun25);
        Assert.StartsWith("2026.", s);
        Assert.EndsWith("19–25.", s.Replace("19 –", "19–"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(s, "2026"));
    }

    [Fact]
    public void DayFirstLanguages_DoNotRepeatTheMonthOrOrphanTheYear()
    {
        var s = Range("fr", Mon19, Sun25);       // 19 – 25 oct. 2026
        Assert.StartsWith("19 – 25", s);
        Assert.EndsWith("2026", s);
        Assert.StartsWith("19. – 25.", Range("de", Mon19, Sun25));
    }

    [Fact]
    public void EveryLanguage_ShowsBothEndDays_AndTheYear()
    {
        foreach (var lang in LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode))
        {
            var same = Range(lang.Code, Mon19, Sun25);
            Assert.Contains("19", same);
            Assert.Contains("25", same);
            Assert.DoesNotContain("{", same);
            var cross = Range(lang.Code, Mon26, Sun1);
            Assert.Contains("26", cross);
            Assert.Contains("1", cross);
            Assert.DoesNotContain("{", cross);
        }
    }
}

