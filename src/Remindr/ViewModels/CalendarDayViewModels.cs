using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>One dot under a day number. A null colour means "uncategorized" (drawn in the neutral brush).</summary>
public sealed record CalendarDot(string? Color)
{
    public bool IsNeutral => Color is null;

    /// <summary>UI Automation names the item wrapper from ToString (the default record text read as junk).</summary>
    public override string ToString() => Loc.Get(IsNeutral ? "Calendar_DotUncategorized" : "Calendar_DotCategorized");
}

/// <summary>One cell of the month grid.</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    public CalendarDayViewModel(DateOnly date, bool isToday, bool isInMonth, bool isSelected, DaySummary summary)
    {
        Date = date;
        DayNumber = date.Day.ToString(Loc.Culture);
        IsToday = isToday;
        IsInMonth = isInMonth;
        _isSelected = isSelected;
        Dots = summary.DotColors.Select(c => new CalendarDot(c)).ToList();
        HasMoreDots = summary.HasMoreDots;
        HasImportant = summary.HasImportant;
        Count = summary.Count;

        AutomationName = DayAutomationName(date, Count, HasImportant);
    }

    /// <summary>"Saturday, October 24, 2026, 2 reminders, important": what a screen reader says for a day.</summary>
    public static string DayAutomationName(DateOnly date, int count, bool important)
    {
        var day = date.ToString(Loc.Get("Fmt_FullDateYear"), Loc.Culture);
        var amount = Loc.Plural("Calendar_DayAutomationCount", count);
        return Loc.Format(important ? "Calendar_DayAutomationImportant" : "Calendar_DayAutomation", day, amount);
    }

    public DateOnly Date { get; }
    public string DayNumber { get; }
    public bool IsToday { get; }
    public bool IsInMonth { get; }
    public IReadOnlyList<CalendarDot> Dots { get; }
    public bool HasMoreDots { get; }
    public bool HasImportant { get; }
    public int Count { get; }
    public string AutomationName { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>One column of the week view: a date and its (time-sorted) cards.</summary>
public sealed partial class CalendarWeekColumnViewModel : ObservableObject
{
    public CalendarWeekColumnViewModel(DateOnly date, string weekday, bool isToday, bool isSelected,
        IReadOnlyList<ReminderItemViewModel> items)
    {
        Date = date;
        Weekday = weekday;
        DayNumber = date.Day.ToString(Loc.Culture);
        IsToday = isToday;
        _isSelected = isSelected;
        Items = items;
        AutomationName = CalendarDayViewModel.DayAutomationName(date, items.Count, items.Any(i => i.IsImportant));
    }

    public DateOnly Date { get; }
    public string Weekday { get; }
    public string DayNumber { get; }
    public bool IsToday { get; }
    public IReadOnlyList<ReminderItemViewModel> Items { get; }
    public string AutomationName { get; }

    [ObservableProperty] private bool _isSelected;
}

