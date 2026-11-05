using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>A category filter chip. Checking one unchecks the others (the view model does that).</summary>
public sealed partial class CategoryChipViewModel : ObservableObject
{
    public CategoryChipViewModel(Category category) => Category = category;
    public Category Category { get; }
    [ObservableProperty] private bool _isSelected;
}

public partial class TodayViewModel : ObservableObject
{
    private readonly ProfileSession _session;
    private readonly ReminderDialogs _dialogs;
    private readonly ReminderRepo _repo;
    private readonly CategoryRepo _categories;
    private readonly IClock _clock;
    private readonly ReminderActionHandler _actions;
    private HashSet<int> _known = new();
    private bool _reloading;

    public TodayViewModel(ProfileSession session, ReminderDialogs dialogs, ReminderRepo repo, CategoryRepo categories, IClock clock)
    {
        _session = session;
        _dialogs = dialogs;
        _repo = repo;
        _categories = categories;
        _clock = clock;
        _actions = new ReminderActionHandler(repo, dialogs, () => Reload(), leavesOnComplete: true);
        _actions.Saved = r => ReminderHighlight.Apply(Sections.SelectMany(g => g.Items), r);

        repo.Changed += (_, _) => { if (!_actions.IsWriting) Reload(); };
        session.CurrentProfileChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Greeting));
            Reload(animateAll: true);
        };
        // Strings built here (not bound through {l:Loc}) are recomputed when the language changes.
        Loc.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(DateCaption));
            Reload();
        };
    }

    /// <summary>"Good morning, Alex" (before 12:00), "Good afternoon" (before 18:00), otherwise "Good evening".</summary>
    public string Greeting => GreetingFor(DateTime.Now, _session.Current?.Name);

    public string DateCaption => DateTime.Now.ToString(Loc.Get("Fmt_FullDate"), Loc.Culture);

    public ObservableCollection<ReminderGroupViewModel> Sections { get; } = new();
    public ObservableCollection<CategoryChipViewModel> Categories { get; } = new();

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _importantOnly;
    [ObservableProperty] private bool _hideCompleted;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasSections;

    public bool IsEmpty => !HasSections;

    /// <summary>How long a just-completed card stays in place before it moves on. The page sets 0 when animations are off.</summary>
    public TimeSpan SettleDelay
    {
        get => _actions.SettleDelay;
        set => _actions.SettleDelay = value;
    }

    partial void OnImportantOnlyChanged(bool value) => Reload();
    partial void OnHideCompletedChanged(bool value) => Reload();

    private int? SelectedCategoryId => Categories.FirstOrDefault(c => c.IsSelected)?.Category.Id;

    /// <summary>Rebuilds the sections from the database. <paramref name="animateAll"/> replays the entrance for every card.</summary>
    public void Reload(bool animateAll = false)
    {
        if (_reloading) return;
        _reloading = true;
        try
        {
            Sections.Clear();
            if (_session.Current is not { } profile)
            {
                Summary = "";
                HasSections = false;
                Categories.Clear();
                return;
            }

            var cats = _categories.ListByProfile(profile.Id);
            if (!cats.Select(c => c.Id).SequenceEqual(Categories.Select(c => c.Category.Id)))
            {
                var keep = SelectedCategoryId;
                Categories.Clear();
                foreach (var c in cats)
                {
                    var chip = new CategoryChipViewModel(c) { IsSelected = c.Id == keep };
                    chip.PropertyChanged += (_, e) => OnChipChanged(chip, e.PropertyName);
                    Categories.Add(chip);
                }
            }

            var all = _repo.ListAll();
            var now = _clock.Now;
            var buckets = SummaryService.Buckets(all, now);
            Summary = SummaryService.Sentence(buckets);

            var byId = cats.ToDictionary(c => c.Id);
            var category = SelectedCategoryId;
            void Add(ReminderGroupKind kind, string title, IReadOnlyList<Reminder> source, bool showDate)
            {
                var items = source
                    .Where(r => (category is null || r.CategoryId == category) && (!ImportantOnly || r.IsImportant) && (!HideCompleted || !r.IsCompleted))
                    .Select(r => new ReminderItemViewModel(r, r.CategoryId is { } id && byId.TryGetValue(id, out var c) ? c : null,
                        ReminderQueries.WhenText(r, now, showDate), animateAll || !_known.Contains(r.Id), _actions)).ToList();
                if (items.Count > 0) Sections.Add(new ReminderGroupViewModel(kind, title, items, true, null));
            }
            Add(ReminderGroupKind.Overdue, Loc.Get("Group_Overdue"), buckets.Overdue, true);
            Add(ReminderGroupKind.Today, Loc.Get("Group_Today"), buckets.Today, false);
            Add(ReminderGroupKind.Date, Loc.Get("Today_Next7"), buckets.Next7, true);
            Add(ReminderGroupKind.Date, Loc.Get("Common_Important"), buckets.Important, true);

            _known = all.Select(r => r.Id).ToHashSet();
            HasSections = Sections.Count > 0;
        }
        finally { _reloading = false; }
    }

    private void OnChipChanged(CategoryChipViewModel chip, string? property)
    {
        if (property != nameof(CategoryChipViewModel.IsSelected) || _reloading) return;
        if (chip.IsSelected)
        {
            _reloading = true; // unchecking the others must not reload once each
            try { foreach (var other in Categories.Where(c => c != chip && c.IsSelected).ToList()) other.IsSelected = false; }
            finally { _reloading = false; }
        }
        Reload();
    }

    [RelayCommand]
    private async Task NewReminderAsync() => ReminderHighlight.Apply(Sections.SelectMany(g => g.Items), await _dialogs.ShowEditAsync(null));

    public static string GreetingFor(DateTime now, string? name)
    {
        var part = now.Hour < 12 ? "Today_Greeting_Morning" : now.Hour < 18 ? "Today_Greeting_Afternoon" : "Today_Greeting_Evening";
        return string.IsNullOrWhiteSpace(name) ? Loc.Get(part) : Loc.Format(part + "Named", name);
    }
}
