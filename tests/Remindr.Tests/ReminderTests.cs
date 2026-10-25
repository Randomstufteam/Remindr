using System.Globalization;
using System.IO;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.ViewModels;

namespace Remindr.Tests;

public sealed class FakeClock : IClock
{
    public DateTime Now { get; set; }
    public DateTime UtcNow => Now.ToUniversalTime();
    public FakeClock(DateTime now) => Now = now;
}

internal sealed class ReminderEnv : IDisposable
{
    public TempDb T { get; } = new();
    public FakeClock Clock { get; }
    public ProfileRepo Profiles { get; }
    public ProfileSession Session { get; }
    public ReminderRepo Repo { get; }
    public Profile A { get; }
    public Profile B { get; }

    public ReminderEnv(DateTime? now = null)
    {
        new DbInitializer(T.Db).Initialize();
        Clock = new FakeClock(now ?? new DateTime(2026, 10, 24, 10, 30, 15)); // a Saturday
        Profiles = new ProfileRepo(T.Db, Path.Combine(T.Dir, "avatars"));
        Session = new ProfileSession(Profiles, new SettingsRepo(T.Db));
        Repo = new ReminderRepo(T.Db, Session, Clock);
        A = Profiles.Create("A", "#EF4444", "A");
        B = Profiles.Create("B", "#10B981", "B");
        Session.SwitchTo(A);
    }

    public static Reminder R(string title, string date, string? time = null, bool done = false) => new()
    {
        Title = title,
        DueDate = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DueTime = time is null ? null : TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture),
        IsCompleted = done
    };

    public void Dispose() => T.Dispose();
}

public class ReminderRepoTests
{
    [Fact]
    public void Create_ThenGet_RoundTripsAllFields()
    {
        using var e = new ReminderEnv();
        var cat = new CategoryRepo(e.T.Db).ListByProfile(e.A.Id)[1];
        var r = ReminderEnv.R("Dentist", "2026-10-27", "15:00");
        r.Notes = "  bring card  ";
        r.IsImportant = true;
        r.CategoryId = cat.Id;

        var saved = e.Repo.Create(r);
        var got = e.Repo.Get(saved.Id)!;

        Assert.Equal("Dentist", got.Title);
        Assert.Equal("bring card", got.Notes);
        Assert.Equal(new DateOnly(2026, 10, 27), got.DueDate);
        Assert.Equal(new TimeOnly(15, 0), got.DueTime);
        Assert.True(got.IsImportant);
        Assert.False(got.IsCompleted);
        Assert.Null(got.CompletedAt);
        Assert.Equal(cat.Id, got.CategoryId);
        Assert.Equal(e.A.Id, got.ProfileId);
        Assert.StartsWith("2026-10-24T", got.CreatedAt);
    }

    [Fact]
    public void Dates_AreStoredAsPlanTextAndNullTimeMeansAllDay()
    {
        using var e = new ReminderEnv();
        var timed = e.Repo.Create(ReminderEnv.R("Timed", "2026-03-05", "09:05"));
        var allDay = e.Repo.Create(ReminderEnv.R("All day", "2026-03-05"));

        using var conn = e.T.Db.Open();
        var rows = Dapper.SqlMapper.Query(conn, "SELECT id, due_date, due_time FROM reminders ORDER BY id").ToList();
        Assert.Equal("2026-03-05", (string)rows[0].due_date);
        Assert.Equal("09:05", (string)rows[0].due_time);
        Assert.Null((object?)rows[1].due_time);
        Assert.Null(e.Repo.Get(allDay.Id)!.DueTime);
        Assert.True(e.Repo.Get(allDay.Id)!.IsAllDay);
        Assert.NotNull(e.Repo.Get(timed.Id)!.DueTime);
    }

    [Fact]
    public void Create_BlankTitle_Throws()
    {
        using var e = new ReminderEnv();
        Assert.Throws<ArgumentException>(() => e.Repo.Create(ReminderEnv.R("   ", "2026-10-27")));
    }

    [Fact]
    public void Update_ChangesEditableFields_AndRaisesChanged()
    {
        using var e = new ReminderEnv();
        var r = e.Repo.Create(ReminderEnv.R("Old", "2026-10-27", "15:00"));
        var changed = 0;
        e.Repo.Changed += (_, _) => changed++;

        r.Title = "New";
        r.Notes = null;
        r.DueDate = new DateOnly(2026, 11, 2);
        r.DueTime = null;
        r.IsImportant = true;
        Assert.True(e.Repo.Update(r));

        var got = e.Repo.Get(r.Id)!;
        Assert.Equal("New", got.Title);
        Assert.Null(got.Notes);
        Assert.Equal(new DateOnly(2026, 11, 2), got.DueDate);
        Assert.Null(got.DueTime);
        Assert.True(got.IsImportant);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Delete_RemovesOnlyThatReminder()
    {
        using var e = new ReminderEnv();
        var a = e.Repo.Create(ReminderEnv.R("a", "2026-10-27"));
        var b = e.Repo.Create(ReminderEnv.R("b", "2026-10-27"));
        Assert.True(e.Repo.Delete(a.Id));
        Assert.False(e.Repo.Delete(a.Id));
        Assert.Null(e.Repo.Get(a.Id));
        Assert.NotNull(e.Repo.Get(b.Id));
    }

    [Fact]
    public void ListByRange_IsInclusive_SortedAllDayFirst()
    {
        using var e = new ReminderEnv();
        e.Repo.Create(ReminderEnv.R("before", "2026-10-26", "10:00"));
        e.Repo.Create(ReminderEnv.R("late", "2026-10-27", "18:00"));
        e.Repo.Create(ReminderEnv.R("early", "2026-10-27", "08:00"));
        e.Repo.Create(ReminderEnv.R("allday", "2026-10-27"));
        e.Repo.Create(ReminderEnv.R("last", "2026-10-29", "23:59"));
        e.Repo.Create(ReminderEnv.R("after", "2026-10-30"));

        var list = e.Repo.ListByRange(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 29));
        Assert.Equal(new[] { "allday", "early", "late", "last" }, list.Select(r => r.Title).ToArray());
    }

    [Fact]
    public void ListOverdue_IncompleteAndBeforeNow()
    {
        using var e = new ReminderEnv(); // now = Sat 2026-10-24 10:30:15
        e.Repo.Create(ReminderEnv.R("yesterday all-day", "2026-10-23"));
        e.Repo.Create(ReminderEnv.R("today earlier", "2026-10-24", "09:00"));
        e.Repo.Create(ReminderEnv.R("today exactly now minute", "2026-10-24", "10:30"));
        e.Repo.Create(ReminderEnv.R("today later", "2026-10-24", "11:00"));
        e.Repo.Create(ReminderEnv.R("today all-day", "2026-10-24"));
        e.Repo.Create(ReminderEnv.R("tomorrow", "2026-10-25", "08:00"));
        e.Repo.Create(ReminderEnv.R("old but done", "2026-10-01", "08:00", done: true));

        var overdue = e.Repo.ListOverdue(e.Clock.Now).Select(r => r.Title).ToArray();
        Assert.Equal(new[] { "yesterday all-day", "today earlier" }, overdue);
    }

    [Fact]
    public void SetCompleted_StampsAndClearsCompletedAt()
    {
        using var e = new ReminderEnv();
        var r = e.Repo.Create(ReminderEnv.R("x", "2026-10-27", "15:00"));

        Assert.True(e.Repo.SetCompleted(r.Id, true));
        var done = e.Repo.Get(r.Id)!;
        Assert.True(done.IsCompleted);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", done.CompletedAt);

        Assert.True(e.Repo.SetCompleted(r.Id, false));
        var again = e.Repo.Get(r.Id)!;
        Assert.False(again.IsCompleted);
        Assert.Null(again.CompletedAt);
    }

    [Fact]
    public void SetImportant_Toggles()
    {
        using var e = new ReminderEnv();
        var r = e.Repo.Create(ReminderEnv.R("x", "2026-10-27"));
        Assert.True(e.Repo.SetImportant(r.Id, true));
        Assert.True(e.Repo.Get(r.Id)!.IsImportant);
        Assert.True(e.Repo.SetImportant(r.Id, false));
        Assert.False(e.Repo.Get(r.Id)!.IsImportant);
        Assert.False(e.Repo.SetImportant(9999, true));
    }

    [Fact]
    public void ProfileScoping_ProfileACannotSeeOrChangeB()
    {
        using var e = new ReminderEnv();
        var a = e.Repo.Create(ReminderEnv.R("A's", "2026-10-20", "09:00"));
        e.Session.SwitchTo(e.B);
        var b = e.Repo.Create(ReminderEnv.R("B's", "2026-10-20", "09:00"));

        // As B: only B's rows; A's id is invisible and untouchable.
        Assert.Equal(new[] { "B's" }, e.Repo.ListAll().Select(r => r.Title).ToArray());
        Assert.Single(e.Repo.ListByRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        Assert.Single(e.Repo.ListOverdue(e.Clock.Now));
        Assert.Null(e.Repo.Get(a.Id));
        Assert.False(e.Repo.Delete(a.Id));
        Assert.False(e.Repo.SetCompleted(a.Id, true));
        Assert.False(e.Repo.SetImportant(a.Id, true));
        var hijack = a.Clone(); hijack.Title = "hacked";
        Assert.False(e.Repo.Update(hijack));

        e.Session.SwitchTo(e.A);
        Assert.Equal("A's", e.Repo.Get(a.Id)!.Title);
        Assert.False(e.Repo.Get(a.Id)!.IsCompleted);
        Assert.Null(e.Repo.Get(b.Id));
    }

    [Fact]
    public void NoProfile_Throws()
    {
        using var e = new ReminderEnv();
        e.Session.Clear();
        Assert.Throws<InvalidOperationException>(() => e.Repo.ListAll());
    }
}

public class ReminderGroupingTests
{
    // Saturday 2026-10-24 10:30
    private static readonly DateTime Now = new(2026, 10, 24, 10, 30, 0);
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    private static Reminder R(string title, string date, string? time = null, bool done = false, string? doneAt = null, bool imp = false)
    {
        var r = ReminderEnv.R(title, date, time, done);
        r.CompletedAt = doneAt;
        r.IsImportant = imp;
        return r;
    }

    [Fact]
    public void Groups_AreOrdered_OverdueTodayTomorrowDatesCompleted()
    {
        var items = new[]
        {
            R("done1", "2026-10-20", "09:00", true, "2026-10-21T10:00:00Z"),
            R("later", "2026-11-03"),
            R("tomorrow", "2026-10-25", "08:00"),
            R("today", "2026-10-24", "14:00"),
            R("overdue-old", "2026-10-22"),
            R("overdue-today", "2026-10-24", "09:00"),
            R("done2", "2026-10-24", "08:00", true, "2026-10-24T08:30:00Z"),
            R("mid", "2026-10-27", "08:00"),
        };

        var groups = ReminderQueries.Group(items, Now, En);

        Assert.Equal(new[]
        {
            ReminderGroupKind.Overdue, ReminderGroupKind.Today, ReminderGroupKind.Tomorrow,
            ReminderGroupKind.Date, ReminderGroupKind.Date, ReminderGroupKind.Completed
        }, groups.Select(g => g.Kind).ToArray());
        Assert.Equal(new[] { "Overdue", "Today", "Tomorrow", "Tuesday, Oct 27", "Tuesday, Nov 3", "Completed" },
            groups.Select(g => g.Title).ToArray());
        Assert.Equal(new[] { "overdue-old", "overdue-today" }, groups[0].Items.Select(r => r.Title).ToArray());
        Assert.Equal(new[] { "today" }, groups[1].Items.Select(r => r.Title).ToArray());
        // Completed: most recently completed first.
        Assert.Equal(new[] { "done2", "done1" }, groups[5].Items.Select(r => r.Title).ToArray());
    }

    [Fact]
    public void TodayAllDay_IsNotOverdue_AndSortsFirst()
    {
        var groups = ReminderQueries.Group(new[]
        {
            R("timed", "2026-10-24", "23:00"),
            R("allday", "2026-10-24"),
            R("timed-important", "2026-10-24", "23:00", imp: true),
        }, Now, En);

        var today = Assert.Single(groups);
        Assert.Equal(ReminderGroupKind.Today, today.Kind);
        Assert.Equal(new[] { "allday", "timed-important", "timed" }, today.Items.Select(r => r.Title).ToArray());
    }

    [Fact]
    public void CompletedItemsNeverCountAsOverdue_AndEmptyGroupsAreOmitted()
    {
        var groups = ReminderQueries.Group(new[] { R("old done", "2020-01-01", "08:00", true, "2020-01-01T09:00:00Z") }, Now, En);
        Assert.Equal(ReminderGroupKind.Completed, Assert.Single(groups).Kind);
        Assert.Empty(ReminderQueries.Group(Array.Empty<Reminder>(), Now, En));
    }

    [Fact]
    public void FarDateHeading_IncludesYearWhenNotCurrent()
    {
        var groups = ReminderQueries.Group(new[] { R("next year", "2027-01-05") }, Now, En);
        Assert.Equal("Tuesday, Jan 5, 2027", groups[0].Title);
    }

    [Fact]
    public void IsOverdue_MatchesMinuteGranularity()
    {
        var r = R("x", "2026-10-24", "10:30");
        Assert.False(ReminderQueries.IsOverdue(r, new DateTime(2026, 10, 24, 10, 30, 45)));
        Assert.True(ReminderQueries.IsOverdue(r, new DateTime(2026, 10, 24, 10, 31, 0)));
    }
}

public class ReminderLabelTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("2026-10-27", "Tuesday")]
    [InlineData("2026-10-24", "Saturday")]
    [InlineData("2026-10-25", "Sunday")]
    [InlineData("2028-02-29", "Tuesday")]
    public void DayOfWeekLabel_NamesTheDay(string date, string expected) =>
        Assert.Equal(expected, ReminderQueries.DayOfWeekLabel(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), En));

    [Fact]
    public void TimeLabel_AllDayOrShortTime()
    {
        Assert.Equal("All day", ReminderQueries.TimeLabel(null, En));
        Assert.Equal("3:00 PM", ReminderQueries.TimeLabel(new TimeOnly(15, 0), En));
    }

    [Fact]
    public void DefaultNewSlot_NextHour_RollsToTomorrowNineAfterTheLastHour()
    {
        Assert.Equal((new DateOnly(2026, 10, 24), new TimeOnly(11, 0)),
            ReminderQueries.DefaultNewSlot(new DateTime(2026, 10, 24, 10, 30, 0)));
        Assert.Equal((new DateOnly(2026, 10, 25), new TimeOnly(9, 0)),
            ReminderQueries.DefaultNewSlot(new DateTime(2026, 10, 24, 23, 30, 0)));
        Assert.Equal((new DateOnly(2026, 11, 20), new TimeOnly(9, 0)),
            ReminderQueries.DefaultNewSlot(new DateTime(2026, 10, 24, 10, 30, 0), new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void TimeSuggestions_Are96QuarterHours()
    {
        var s = ReminderQueries.TimeSuggestions(En);
        Assert.Equal(96, s.Count);
        Assert.Equal("12:00 AM", s[0]);
        Assert.Equal("3:15 PM", s[61]);
    }

    [Theory]
    [InlineData("3pm", 15, 0)]
    [InlineData("3:30 PM", 15, 30)]
    [InlineData("3:30pm", 15, 30)]
    [InlineData("15:30", 15, 30)]
    [InlineData("1530", 15, 30)]
    [InlineData("9", 9, 0)]
    [InlineData("12:00 AM", 0, 0)]
    [InlineData(" 7:05 a.m. ", 7, 5)]
    public void TimeParser_AcceptsCommonForms(string text, int h, int m)
    {
        Assert.True(TimeParser.TryParse(text, out var t, En));
        Assert.Equal(new TimeOnly(h, m), t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("25:00")]
    [InlineData("3:99")]
    public void TimeParser_RejectsGarbage(string text) => Assert.False(TimeParser.TryParse(text, out _, En));
}

public class ReminderEditViewModelTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTime Now = new(2026, 10, 24, 10, 30, 0);
    private static readonly Category[] Cats =
    {
        new() { Id = 5, Name = "School", Color = "#3B82F6" }, new() { Id = 6, Name = "Work", Color = "#F59E0B" }
    };

    [Fact]
    public void New_DefaultsToToday_NextHour_NoCategory()
    {
        var vm = new ReminderEditViewModel(null, null, Now, Cats, En);
        Assert.Equal("Saturday", vm.DayOfWeekText);
        Assert.Equal("11:00 AM", vm.TimeText);
        Assert.False(vm.IsAllDay);
        Assert.Null(vm.SelectedCategory.Id);
        Assert.Equal(new[] { "None", "School", "Work" }, vm.Categories.Select(c => c.Name).ToArray());
        Assert.False(vm.IsValid);            // title required
        Assert.Null(vm.ShownError);          // but not nagged before the first edit
    }

    [Fact]
    public void DayOfWeek_FollowsTheDate()
    {
        var vm = new ReminderEditViewModel(null, null, Now, Cats, En) { Title = "Dentist" };
        vm.DueDate = new DateTime(2026, 10, 27);
        Assert.Equal("Tuesday", vm.DayOfWeekText);
        vm.DueDate = null;
        Assert.Equal("", vm.DayOfWeekText);
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void Validation_TitleAndTime()
    {
        var vm = new ReminderEditViewModel(null, null, Now, Cats, En) { Title = "x" };
        Assert.True(vm.IsValid);
        vm.TimeText = "banana";
        Assert.False(vm.IsValid);
        vm.IsAllDay = true;                  // time is ignored for all-day
        Assert.True(vm.IsValid);
        vm.Title = "   ";
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void ApplyTo_WritesFieldsToTheReminder()
    {
        var vm = new ReminderEditViewModel(null, new DateOnly(2026, 10, 27), Now, Cats, En)
        {
            Title = "  Dentist ", Notes = " bring card ", TimeText = "3pm", IsImportant = true
        };
        vm.SelectedCategory = vm.Categories.First(c => c.Id == 6);

        var r = new Reminder();
        vm.ApplyTo(r);

        Assert.Equal("Dentist", r.Title);
        Assert.Equal("bring card", r.Notes);
        Assert.Equal(new DateOnly(2026, 10, 27), r.DueDate);
        Assert.Equal(new TimeOnly(15, 0), r.DueTime);
        Assert.True(r.IsImportant);
        Assert.Equal(6, r.CategoryId);

        vm.IsAllDay = true;
        vm.ApplyTo(r);
        Assert.Null(r.DueTime);
    }

    [Fact]
    public void Edit_LoadsExistingValues_AllDayKeepsASensibleTime()
    {
        var existing = new Reminder
        {
            Id = 1, Title = "Pay rent", Notes = "n", DueDate = new DateOnly(2026, 11, 1), DueTime = null,
            IsImportant = true, CategoryId = 5
        };
        var vm = new ReminderEditViewModel(existing, null, Now, Cats, En);
        Assert.Equal("Pay rent", vm.Title);
        Assert.True(vm.IsAllDay);
        Assert.False(vm.TimeEnabled);
        Assert.Equal("9:00 AM", vm.TimeText);
        Assert.True(vm.IsImportant);
        Assert.Equal(5, vm.SelectedCategory.Id);
        Assert.Equal("Sunday", vm.DayOfWeekText);
        Assert.True(vm.IsValid);
    }
}


