using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>One entry of the category combo. Id null = "None".</summary>
public sealed record CategoryOption(int? Id, string Name, string? Color)
{
    public bool HasColor => Color is not null;
    public static CategoryOption None => new(null, Loc.Get("Category_None"), null);
}

/// <summary>State and validation for the create/edit reminder dialog. No WPF types, so it can be unit tested.</summary>
public partial class ReminderEditViewModel : ObservableObject
{
    public const int MaxTitleLength = 120;

    private readonly CultureInfo _culture;
    private bool _touched;

    public ReminderEditViewModel(Reminder? existing, DateOnly? defaultDate, DateTime now,
        IReadOnlyList<Category> categories, CultureInfo? culture = null)
    {
        _culture = culture ?? CultureInfo.CurrentCulture;
        Existing = existing;
        Categories = new[] { CategoryOption.None }
            .Concat(categories.Select(c => new CategoryOption(c.Id, c.Name, c.Color))).ToList();
        TimeSuggestions = ReminderQueries.TimeSuggestions(_culture);
        Picker = new PicturePickerViewModel(
            PictureCatalog.ForReminders().OrderBy(e => e.ReminderGroup),
            Enum.GetValues<ReminderCategory>().Select(c => (PictureCatalog.CategoryLabel(c), (Func<PictureEntry, bool>)(e => e.ReminderGroup == c))),
            existing?.Icon);
        Picker.Chosen += ChooseIcon;

        if (existing is null)
        {
            var (date, time) = ReminderQueries.DefaultNewSlot(now, defaultDate);
            _dueDate = date.ToDateTime(TimeOnly.MinValue);
            _timeText = time.ToString("t", _culture);
            _selectedCategory = CategoryOption.None;
            _title = "";
            _notes = "";
        }
        else
        {
            _title = existing.Title;
            _notes = existing.Notes ?? "";
            _dueDate = existing.DueDate.ToDateTime(TimeOnly.MinValue);
            _isAllDay = existing.DueTime is null;
            _timeText = (existing.DueTime ?? new TimeOnly(9, 0)).ToString("t", _culture);
            _isImportant = existing.IsImportant;
            _icon = existing.Icon;
            _selectedCategory = Categories.FirstOrDefault(c => c.Id == existing.CategoryId) ?? CategoryOption.None;
        }
    }

    public Reminder? Existing { get; }
    public bool IsNew => Existing is null;
    public IReadOnlyList<CategoryOption> Categories { get; }
    public IReadOnlyList<string> TimeSuggestions { get; }

    /// <summary>Category chips + search over the reminder pictures (the "Add picture" popup).</summary>
    public PicturePickerViewModel Picker { get; }

    /// <summary>Stored picture value ('builtin:key') or null for "No picture".</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIcon), nameof(IconButtonName))]
    private string? _icon;

    /// <summary>True only for a key in the catalog; an unknown key (e.g. from a newer version) shows the "add" glyph but is kept on save.</summary>
    public bool HasIcon => PictureCatalog.FromValue(Icon) is not null;

    /// <summary>Accessible name of the picture button: names the current picture, or invites adding one.</summary>
    public string IconButtonName =>
        PictureCatalog.FromValue(Icon) is { } e ? Loc.Format("Reminder_PictureChange", e.Label) : Loc.Get("Reminder_PictureAdd");

    public void ChooseIcon(PictureOption option)
    {
        Icon = option.Entry.Value;
        Picker.SelectValue(Icon);
    }

    public void ClearIcon()
    {
        Icon = null;
        Picker.SelectValue(null);
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error), nameof(ShownError))]
    private string _title;

    [ObservableProperty] private string _notes;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error), nameof(ShownError), nameof(DayOfWeekText))]
    private DateTime? _dueDate;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error), nameof(ShownError), nameof(TimeEnabled))]
    private bool _isAllDay;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error), nameof(ShownError))]
    private string _timeText;

    [ObservableProperty] private bool _isImportant;

    [ObservableProperty] private CategoryOption _selectedCategory;

    public bool TimeEnabled => !IsAllDay;

    /// <summary>"Tuesday" for the chosen date, empty when no date is set.</summary>
    public string DayOfWeekText =>
        DueDate is { } d ? ReminderQueries.DayOfWeekLabel(DateOnly.FromDateTime(d), _culture) : "";

    partial void OnTitleChanged(string value) => _touched = true;

    public string? Error
    {
        get
        {
            var t = Title.Trim();
            if (t.Length == 0) return Loc.Get("Reminder_Error_Title");
            if (t.Length > MaxTitleLength) return Loc.Format("Reminder_Error_TitleLength", MaxTitleLength);
            if (DueDate is null) return Loc.Get("Reminder_Error_Date");
            if (!IsAllDay && !TimeParser.TryParse(TimeText, out _, _culture))
                return Loc.Format("Reminder_Error_Time", new TimeOnly(15, 30).ToString("t", _culture));
            return null;
        }
    }

    public bool IsValid => Error is null;

    /// <summary>Error text for display; hides the empty-title error until the title has been edited.</summary>
    public string? ShownError => (!_touched && Title.Trim().Length == 0) ? null : Error;

    /// <summary>Copies the dialog's values onto <paramref name="target"/>. Call only when <see cref="IsValid"/>.</summary>
    public void ApplyTo(Reminder target)
    {
        target.Title = Title.Trim();
        target.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        target.DueDate = DateOnly.FromDateTime(DueDate!.Value);
        target.DueTime = IsAllDay ? null : (TimeParser.TryParse(TimeText, out var t, _culture) ? t : null);
        target.IsImportant = IsImportant;
        target.CategoryId = SelectedCategory.Id;
        target.Icon = Icon;
    }
}


