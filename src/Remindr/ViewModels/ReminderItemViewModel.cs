using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>What a reminder card can do. Implemented by the list view model.</summary>
public interface IReminderActions
{
    void CompletedChanged(ReminderItemViewModel item);
    void ImportantChanged(ReminderItemViewModel item);
    Task EditAsync(ReminderItemViewModel item);
    Task DeleteAsync(ReminderItemViewModel item);
}

/// <summary>One reminder card in the list.</summary>
public partial class ReminderItemViewModel : ObservableObject
{
    private readonly IReminderActions _actions;

    public ReminderItemViewModel(Reminder reminder, Category? category, string whenText, bool animate, IReminderActions actions,
        bool compact = false)
    {
        Reminder = reminder;
        IsCompact = compact;
        _actions = actions;
        WhenText = whenText;
        Animate = animate;
        CategoryName = category?.Name;
        CategoryColor = category?.Color;
        _isCompleted = reminder.IsCompleted;
        _isImportant = reminder.IsImportant;
        EditCommand = new AsyncRelayCommand(() => actions.EditAsync(this));
        DeleteCommand = new AsyncRelayCommand(() => actions.DeleteAsync(this));
    }

    public Reminder Reminder { get; }
    public int Id => Reminder.Id;
    public string Title => Reminder.Title;
    /// <summary>Accessible name of the complete checkbox, e.g. "Complete: Dentist".</summary>
    public string CompleteAutomationName => Loc.Format("Card_CompleteName", Reminder.Title);
    public string? Notes => Reminder.Notes;
    /// <summary>True when the reminder has a picture whose key is in the catalog; unknown keys show nothing.</summary>
    public bool HasIcon => PictureCatalog.FromValue(Reminder.Icon) is not null;
    public string IconLabel => PictureCatalog.FromValue(Reminder.Icon)?.Label ?? "";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Reminder.Notes);

    /// <summary>"3:00 PM" or "All day" (overdue items also carry their date).</summary>
    public string WhenText { get; }

    public string? CategoryName { get; }
    public string? CategoryColor { get; }
    public bool HasCategory => CategoryName is not null;

    /// <summary>Fade in when created. False for items that were already on screen, so a refresh does not replay the entrance.</summary>
    public bool Animate { get; }

    /// <summary>Narrow layout for the week columns: no notes, flag or hover buttons (edit/delete move to a right-click menu).</summary>
    public bool IsCompact { get; }

    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isImportant;

    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    // Keep the snapshot in sync: the list does not rebuild after its own quick toggles, and Edit opens the dialog from
    // Reminder, so a stale IsImportant here would be shown (and saved back) by the edit dialog.
    partial void OnIsCompletedChanged(bool value)
    {
        Reminder.IsCompleted = value;
        _actions.CompletedChanged(this);
    }

    partial void OnIsImportantChanged(bool value)
    {
        Reminder.IsImportant = value;
        _actions.ImportantChanged(this);
    }
}

/// <summary>A titled section of cards (Overdue, Today, a date, Completed).</summary>
public partial class ReminderGroupViewModel : ObservableObject
{
    private readonly Action<bool>? _expandedChanged;

    public ReminderGroupViewModel(ReminderGroupKind kind, string title, IReadOnlyList<ReminderItemViewModel> items,
        bool expanded, Action<bool>? expandedChanged)
    {
        Kind = kind;
        Title = title;
        Items = items;
        _isExpanded = expanded;
        _expandedChanged = expandedChanged;
    }

    public ReminderGroupKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<ReminderItemViewModel> Items { get; }
    public bool IsCompletedGroup => Kind == ReminderGroupKind.Completed;
    public bool IsRegularGroup => !IsCompletedGroup;
    public bool IsOverdue => Kind == ReminderGroupKind.Overdue;
    public string CountText => Items.Count.ToString();

    /// <summary>Only the Completed group collapses; everything else is always open.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ItemsVisible))]
    private bool _isExpanded;

    public bool ItemsVisible => !IsCompletedGroup || IsExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value) => _expandedChanged?.Invoke(value);
}


