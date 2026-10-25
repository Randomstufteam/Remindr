using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

public partial class AllRemindersViewModel : ObservableObject
{
    private readonly ReminderRepo _repo;
    private readonly CategoryRepo _categories;
    private readonly ProfileSession _session;
    private readonly IClock _clock;
    private readonly ReminderDialogs _dialogs;
    private readonly ReminderActionHandler _actions;

    private bool _completedExpanded;

    public AllRemindersViewModel(ReminderRepo repo, CategoryRepo categories, ProfileSession session, IClock clock,
        ReminderDialogs dialogs)
    {
        _repo = repo;
        _categories = categories;
        _session = session;
        _clock = clock;
        _dialogs = dialogs;
        _actions = new ReminderActionHandler(repo, dialogs, () => Reload(), leavesOnComplete: true);
        _actions.Saved = r => ReminderHighlight.Apply(Groups.SelectMany(g => g.Items), r);

        repo.Changed += (_, _) => { if (!_actions.IsWriting) Reload(); };
        session.CurrentProfileChanged += (_, _) => Reload(animateAll: true);
        // Group titles, "All day" and the summary are built here, so rebuild the list when the language changes.
        Loc.CultureChanged += (_, _) => Reload();
    }

    public ObservableCollection<ReminderGroupViewModel> Groups { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasReminders))]
    private bool _isEmpty = true;

    public bool HasReminders => !IsEmpty;

    [ObservableProperty] private string _summary = "";

    /// <summary>How long a just-completed card stays in place (showing the strike-through) before it moves to Completed. The page sets 0 when animations are off.</summary>
    public TimeSpan SettleDelay
    {
        get => _actions.SettleDelay;
        set => _actions.SettleDelay = value;
    }

    /// <summary>Rebuilds the list from the database. <paramref name="animateAll"/> replays the entrance for every card (page shown / profile switched).</summary>
    public void Reload(bool animateAll = false)
    {
        Groups.Clear();
        if (_session.Current is not { } profile)
        {
            IsEmpty = true;
            Summary = "";
            return;
        }

        var all = _repo.ListAll();
        var cats = _categories.ListByProfile(profile.Id).ToDictionary(c => c.Id);
        var now = _clock.Now;

        foreach (var g in ReminderQueries.Group(all, now))
        {
            var showDate = g.Kind == ReminderGroupKind.Overdue || g.Kind == ReminderGroupKind.Completed;
            var items = g.Items.Select(r =>
                new ReminderItemViewModel(r, r.CategoryId is { } cid && cats.TryGetValue(cid, out var c) ? c : null,
                    ReminderQueries.WhenText(r, now, showDate), animateAll || !_known.Contains(r.Id), _actions)).ToList();
            Groups.Add(new ReminderGroupViewModel(g.Kind, g.Title, items, _completedExpanded, v => _completedExpanded = v));
        }

        _known = all.Select(r => r.Id).ToHashSet();
        IsEmpty = all.Count == 0;
        var open = all.Count(r => !r.IsCompleted);
        Summary = all.Count == 0 ? "" : Loc.Format("AllReminders_Summary", open, all.Count - open);
    }

    private HashSet<int> _known = new();

    [RelayCommand]
    private async Task NewReminderAsync() => ReminderHighlight.Apply(Groups.SelectMany(g => g.Items), await _dialogs.ShowEditAsync(null));
}


