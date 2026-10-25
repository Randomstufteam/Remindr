using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>
/// Calendar page: Month / Week / Agenda over the same reminders. Only the visible date range is queried. All date math is
/// in <see cref="CalendarMath"/>; this class holds state and builds the cell / column / group view models.
/// </summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly ReminderRepo _repo;
    private readonly CategoryRepo _categories;
    private readonly ProfileSession _session;
    private readonly IClock _clock;
    private readonly ReminderDialogs _dialogs;
    private readonly ReminderActionHandler _actions;

    private DateRange? _range;                       // the range the current data was loaded for
    private IReadOnlyDictionary<DateOnly, IReadOnlyList<Reminder>> _byDay = new Dictionary<DateOnly, IReadOnlyList<Reminder>>();
    private IReadOnlyDictionary<int, Category> _cats = new Dictionary<int, Category>();

    public CalendarViewModel(ReminderRepo repo, CategoryRepo categories, ProfileSession session, IClock clock,
        ReminderDialogs dialogs)
    {
        _repo = repo;
        _categories = categories;
        _session = session;
        _clock = clock;
        _dialogs = dialogs;
        _actions = new ReminderActionHandler(repo, dialogs, () => Reload(), refreshAfterImportant: true);
        _actions.Saved = Highlight;
        _anchor = clock.Today;
        Title = CalendarMath.Title(Mode, _anchor, CalendarMath.FirstDayOfWeek(), Loc.Culture);
        Weekdays = CalendarMath.WeekdayLabels(CalendarMath.FirstDayOfWeek(), Loc.Culture);

        repo.Changed += (_, _) => { if (!_actions.IsWriting) Reload(); };
        session.CurrentProfileChanged += (_, _) => Reload(animate: true);
        // New language: weekday names, first day of the week, titles and day captions all change.
        Loc.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AgendaEmptyText));
            Reload();
        };
    }

    // ---- state -----------------------------------------------------------------------------------------

    /// <summary>Month view: the selected day (its month is shown). Week: any day of the shown week. Agenda: the first day.</summary>
    public DateOnly Anchor => _anchor;
    private DateOnly _anchor;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsMonth), nameof(IsWeek), nameof(IsAgenda))]
    private CalendarViewMode _mode = CalendarViewMode.Month;

    public bool IsMonth { get => Mode == CalendarViewMode.Month; set { if (value) Mode = CalendarViewMode.Month; } }
    public bool IsWeek { get => Mode == CalendarViewMode.Week; set { if (value) Mode = CalendarViewMode.Week; } }
    public bool IsAgenda { get => Mode == CalendarViewMode.Agenda; set { if (value) Mode = CalendarViewMode.Agenda; } }

    partial void OnModeChanged(CalendarViewMode value) => Reload(animate: true);

    [ObservableProperty] private string _title = "";

    /// <summary>Set by the page when it is narrower than about 900 px: the day panel moves below the month grid.</summary>
    [ObservableProperty] private bool _isNarrow;

    /// <summary>Weekday header labels in culture order ("Sun".."Sat" or "Mon".."Sun").</summary>
    [ObservableProperty] private IReadOnlyList<string> _weekdays = Array.Empty<string>();

    /// <summary>"No reminders in the next 30 days."</summary>
    public string AgendaEmptyText => Loc.Format("Calendar_AgendaEmpty", CalendarMath.AgendaDays);

    public ObservableCollection<CalendarDayViewModel> MonthCells { get; } = new();
    public ObservableCollection<CalendarWeekColumnViewModel> WeekColumns { get; } = new();
    public ObservableCollection<ReminderGroupViewModel> AgendaGroups { get; } = new();
    public ObservableCollection<ReminderItemViewModel> SelectedDayItems { get; } = new();

    [ObservableProperty] private string _selectedDayTitle = "";
    [ObservableProperty] private string _selectedDayCaption = "";
    [ObservableProperty] private bool _selectedDayEmpty = true;
    [ObservableProperty] private bool _agendaEmpty = true;

    /// <summary>How long a just-completed card stays put before the view refreshes. The page sets 0 when animations are off.</summary>
    public TimeSpan SettleDelay
    {
        get => _actions.SettleDelay;
        set => _actions.SettleDelay = value;
    }

    // ---- loading ---------------------------------------------------------------------------------------

    /// <summary>Rebuilds the visible view from the database. <paramref name="animate"/> replays the card entrance.</summary>
    public void Reload(bool animate = false)
    {
        MonthCells.Clear();
        WeekColumns.Clear();
        AgendaGroups.Clear();
        SelectedDayItems.Clear();

        var culture = Loc.Culture;
        var first = CalendarMath.FirstDayOfWeek(culture);
        Weekdays = CalendarMath.WeekdayLabels(first, culture);
        Title = CalendarMath.Title(Mode, _anchor, first, culture);
        if (_session.Current is not { } profile)
        {
            _range = null;
            SelectedDayEmpty = true;
            AgendaEmpty = true;
            return;
        }

        var range = CalendarMath.VisibleRange(Mode, _anchor, first);
        _range = range;
        _cats = _categories.ListByProfile(profile.Id).ToDictionary(c => c.Id);
        var reminders = _repo.ListByRange(range.From, range.To);
        _byDay = CalendarMath.GroupByDay(reminders);
        var today = _clock.Today;

        switch (Mode)
        {
            case CalendarViewMode.Month:
                foreach (var d in CalendarMath.MonthGridDates(_anchor, first))
                {
                    var summary = CalendarMath.SummarizeDay(DayItems(d), _cats);
                    MonthCells.Add(new CalendarDayViewModel(d, d == today, CalendarMath.IsSameMonth(d, _anchor), d == _anchor, summary));
                }
                RefreshSelectedDay(animate);
                break;

            case CalendarViewMode.Week:
                var labels = Weekdays;
                var i = 0;
                for (var d = range.From; d <= range.To; d = d.AddDays(1), i++)
                {
                    var items = DayItems(d).Select(r => Item(r, animate, compact: true)).ToList();
                    WeekColumns.Add(new CalendarWeekColumnViewModel(d, labels[i], d == today, d == _anchor, items));
                }
                break;

            default:
                foreach (var day in CalendarMath.Agenda(reminders, range))
                {
                    var items = day.Items.Select(r => Item(r, animate, compact: false)).ToList();
                    AgendaGroups.Add(new ReminderGroupViewModel(ReminderGroupKind.Date, AgendaHeading(day.Date, today), items, true, null));
                }
                AgendaEmpty = AgendaGroups.Count == 0;
                break;
        }
    }

    private IReadOnlyList<Reminder> DayItems(DateOnly d) =>
        _byDay.TryGetValue(d, out var list) ? list : Array.Empty<Reminder>();

    private ReminderItemViewModel Item(Reminder r, bool animate, bool compact) =>
        new(r, r.CategoryId is { } id && _cats.TryGetValue(id, out var c) ? c : null,
            ReminderQueries.WhenText(r, _clock.Now, showDate: false), animate, _actions, compact);

    private string AgendaHeading(DateOnly date, DateOnly today)
    {
        var heading = ReminderQueries.DateHeading(date, today, Loc.Culture);
        return date == today ? Loc.Format("Calendar_TodayHeading", heading)
             : date == today.AddDays(1) ? Loc.Format("Calendar_TomorrowHeading", heading)
             : heading;
    }

    /// <summary>Fills the right-hand panel with the selected day's cards.</summary>
    private void RefreshSelectedDay(bool animate)
    {
        SelectedDayItems.Clear();
        foreach (var r in DayItems(_anchor)) SelectedDayItems.Add(Item(r, animate, compact: false));
        var today = _clock.Today;
        var anchorHeading = ReminderQueries.DateHeading(_anchor, today, Loc.Culture);
        SelectedDayTitle = _anchor == today ? Loc.Format("Calendar_TodayHeading", anchorHeading) : anchorHeading;
        SelectedDayCaption = Loc.Plural("Calendar_DayCaption", SelectedDayItems.Count);
        SelectedDayEmpty = SelectedDayItems.Count == 0;
    }

    // ---- navigation ------------------------------------------------------------------------------------

    /// <summary>Moves the anchor. If the visible range is unchanged (e.g. picking another day of the shown month) only the selection updates.</summary>
    private void SetAnchor(DateOnly date)
    {
        var newRange = CalendarMath.VisibleRange(Mode, date, CalendarMath.FirstDayOfWeek(Loc.Culture));
        var direction = NavigationDirection(_anchor, date);
        _anchor = date;
        OnPropertyChanged(nameof(Anchor));
        if (_range is null || newRange != _range || Mode == CalendarViewMode.Agenda)
        {
            // The view is about to be replaced by another month / week: let the page keep the old one for the slide-out.
            if (_range is not null && newRange != _range && Mode != CalendarViewMode.Agenda && direction != 0)
                Navigating?.Invoke(direction);
            Reload(animate: true);
            return;
        }
        foreach (var c in MonthCells) c.IsSelected = c.Date == date;
        foreach (var c in WeekColumns) c.IsSelected = c.Date == date;
        if (Mode == CalendarViewMode.Month) RefreshSelectedDay(animate: true);
    }

    /// <summary>+1 when moving to a later date, -1 for an earlier one, 0 when the date is unchanged (the slide direction).</summary>
    public static int NavigationDirection(DateOnly from, DateOnly to) => to.CompareTo(from) switch { > 0 => 1, < 0 => -1, _ => 0 };

    /// <summary>Raised just before Month / Week data for another range replaces the current one. Argument: the direction (+1 forward, -1 back).</summary>
    public event Action<int>? Navigating;

    [RelayCommand]
    private void Previous() => SetAnchor(CalendarMath.Navigate(Mode, _anchor, -1));

    [RelayCommand]
    private void Next() => SetAnchor(CalendarMath.Navigate(Mode, _anchor, +1));

    [RelayCommand]
    private void GoToToday() => SetAnchor(_clock.Today);

    /// <summary>Selects a day (month cell or week column).</summary>
    [RelayCommand]
    private void SelectDay(DateOnly date)
    {
        if (Mode != CalendarViewMode.Agenda) SetAnchor(date);
    }

    /// <summary>Arrow keys in the month grid. Parameter: Left, Right, Up or Down.</summary>
    [RelayCommand]
    private void MoveSelection(string? direction)
    {
        if (Mode != CalendarViewMode.Month) return;
        var (dx, dy) = direction switch
        {
            "Left" => (-1, 0),
            "Right" => (1, 0),
            "Up" => (0, -1),
            "Down" => (0, 1),
            _ => (0, 0)
        };
        if (dx == 0 && dy == 0) return;
        SetAnchor(CalendarMath.MoveSelection(_anchor, dx, dy));
    }

    // ---- creating --------------------------------------------------------------------------------------

    /// <summary>New reminder prefilled with the selected / anchor date.</summary>
    [RelayCommand]
    private async Task NewReminderAsync() => Highlight(await _dialogs.ShowEditAsync(null, _anchor));

    /// <summary>New reminder prefilled with a given day (double-click on an empty part of a day, Enter on the selection).</summary>
    [RelayCommand]
    private async Task NewReminderOnAsync(DateOnly date) => Highlight(await _dialogs.ShowEditAsync(null, date));

    /// <summary>Scrolls the saved reminder's card into view and glows it, wherever it is shown (day panel, week column, agenda).</summary>
    private void Highlight(Reminder? saved) => ReminderHighlight.Apply(
        SelectedDayItems.Concat(WeekColumns.SelectMany(c => c.Items)).Concat(AgendaGroups.SelectMany(g => g.Items)), saved);
}


