using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;
using Remindr.Views.Dialogs;

namespace Remindr.Tests;

/// <summary>Phase 5 slices 1-3: the Today buckets and chips, the summary sentence, and the once-a-day summary toast.</summary>
public class TodayTests
{
    // 2026-10-24 is a Saturday, 10:30 (ReminderEnv's default clock).
    private static (ReminderEnv E, TodayViewModel Vm, Category Cat0, Category Cat1) MakeToday()
    {
        var e = new ReminderEnv();
        var cats = new CategoryRepo(e.T.Db);
        var list = cats.ListByProfile(e.A.Id);
        var dialogs = new ReminderDialogs(null!, e.Repo, cats, e.Session, e.Clock);
        var vm = new TodayViewModel(e.Session, dialogs, e.Repo, cats, e.Clock);

        void Add(string title, string date, string? time = null, bool done = false, bool important = false, Category? cat = null)
        {
            var r = ReminderEnv.R(title, date, time, done);
            r.IsImportant = important;
            r.CategoryId = cat?.Id;
            e.Repo.Create(r);
        }
        Add("Late", "2026-10-23");                                         // overdue
        Add("Now", "2026-10-24", "15:00", cat: list[0]);                   // today
        Add("Done", "2026-10-24", "09:00", done: true);                    // today, completed
        Add("Soon", "2026-10-27", "09:00", cat: list[1]);                  // next 7 days
        Add("Tax", "2026-10-28", important: true);                         // next 7 days and important
        Add("Far", "2026-11-30", important: true);                         // outside every bucket
        vm.Reload();
        return (e, vm, list[0], list[1]);
    }

    private static Dictionary<string, string[]> Sections(TodayViewModel vm) =>
        vm.Sections.GroupBy(s => s.Title).ToDictionary(g => g.Key, g => g.First().Items.Select(i => i.Title).ToArray());

    [Fact]
    public void EachBucketShowsItsItem_AndNothingElse()
    {
        var (e, vm, _, _) = MakeToday();
        using var _e = e;
        var s = Sections(vm);
        Assert.Equal(new[] { "Late" }, s["Overdue"]);
        Assert.Equal(new[] { "Done", "Now" }, s["Today"]);
        Assert.Equal(new[] { "Soon", "Tax" }, s["Next 7 days"]);
        Assert.Equal(new[] { "Tax" }, s["Important"]);
        Assert.Equal("1 due today, 1 overdue, 1 important this week", vm.Summary);
    }

    [Fact]
    public void Chips_NarrowTheSections()
    {
        var (e, vm, cat0, cat1) = MakeToday();
        using var _e = e;

        vm.HideCompleted = true;
        Assert.Equal(new[] { "Now" }, Sections(vm)["Today"]);
        vm.HideCompleted = false;

        vm.ImportantOnly = true;
        var s = Sections(vm);
        Assert.Equal(new[] { "Tax" }, s["Next 7 days"]);
        Assert.False(s.ContainsKey("Overdue") || s.ContainsKey("Today"));
        vm.ImportantOnly = false;

        vm.Categories.First(c => c.Category.Id == cat1.Id).IsSelected = true;
        Assert.Equal(new[] { "Soon" }, Assert.Single(Sections(vm)).Value);
        // Choosing another category replaces the first; unchecking shows everything again.
        vm.Categories.First(c => c.Category.Id == cat0.Id).IsSelected = true;
        Assert.Equal(new[] { "Now" }, Assert.Single(Sections(vm)).Value);
        Assert.Single(vm.Categories, c => c.IsSelected);
        vm.Categories.First(c => c.IsSelected).IsSelected = false;
        Assert.Equal(4, vm.Sections.Count);
    }

    [Fact]
    public void Summary_EmptySingularAndPlural()
    {
        Reminder R(string date, bool important = false) { var r = ReminderEnv.R("x", date, "12:00"); r.IsImportant = important; return r; }
        var now = new DateTime(2026, 10, 24, 10, 30, 0);
        string Say(params Reminder[] rs) => SummaryService.Sentence(SummaryService.Buckets(rs, now));

        Assert.Equal("Nothing due today", Say());
        Assert.Equal("Nothing due today", Say(R("2026-12-01")));                       // out of range
        Assert.Equal("1 due today", Say(R("2026-10-24")));
        Assert.Equal("1 overdue", Say(R("2026-10-20")));
        Assert.Equal("3 due today", Say(R("2026-10-24"), R("2026-10-24"), R("2026-10-24")));
        Assert.Equal("3 due today, 2 overdue, 2 important this week",
            Say(R("2026-10-24"), R("2026-10-24"), R("2026-10-24", true), R("2026-10-20"), R("2026-10-23"), R("2026-10-30", true)));
    }

    private sealed class SummarySink : IToastSink
    {
        public readonly List<(string Title, string Body)> Shown = new();
        public Task ShowAsync(Reminder r, PlannedAlert alert, CancellationToken ct) => Task.CompletedTask;
        public Task ShowSummaryAsync(string title, string body, CancellationToken ct) { Shown.Add((title, body)); return Task.CompletedTask; }
    }

    [Fact]
    public async Task DailySummary_FiresOncePerDay_AndNotAgainAfterARestart()
    {
        using var e = new ReminderEnv(new DateTime(2026, 10, 24, 8, 58, 0));
        var settings = new SettingsRepo(e.T.Db);
        var sink = new SummarySink();
        e.Repo.Create(ReminderEnv.R("Due", "2026-10-24", "17:00"));
        SchedulerService Scheduler() => new(e.Repo, new NotificationLogRepo(e.T.Db), sink, e.Clock, new DailySummaryService(settings, e.Repo, e.Session));

        var daily = new DailySummaryService(settings, e.Repo, e.Session);
        var s = Scheduler();
        await s.TickAsync();
        Assert.Empty(sink.Shown);                                   // off by default

        daily.Enabled = true;
        daily.Time = new TimeOnly(9, 0);
        await s.TickAsync();
        Assert.Empty(sink.Shown);                                   // 08:58, not yet

        e.Clock.Now = new DateTime(2026, 10, 24, 9, 0, 30);
        await s.TickAsync();
        await s.TickAsync();
        Assert.Equal(("Daily summary", "1 due today"), Assert.Single(sink.Shown));
        Assert.Equal("2026-10-24", settings.Get(DailySummaryService.LastDateKey));

        e.Clock.Now = new DateTime(2026, 10, 24, 20, 0, 0);
        await Scheduler().TickAsync();                              // "relaunch": a new scheduler on the same database
        Assert.Single(sink.Shown);

        e.Clock.Now = new DateTime(2026, 10, 25, 9, 1, 0);          // next day
        await Scheduler().TickAsync();
        Assert.Equal(2, sink.Shown.Count);
        Assert.Equal("1 overdue", sink.Shown[1].Body);   // yesterday's 17:00 reminder
    }
}
