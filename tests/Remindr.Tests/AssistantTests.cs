using System.IO;
using System.Text.Json;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Services.Assistant;

namespace Remindr.Tests;

/// <summary>Phase 5 part 2, slices 1-3: the local AI assistant's UI-free logic. No model is loaded anywhere in these tests.</summary>
public class AssistantReplayTests
{
    // The model's recorded raw answers (Q4_K_M, every phrase and every comma-list part), saved by the spike with SPIKE_RAW.
    private static readonly Dictionary<string, string> Raw = JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "assistant-raw-q4.json")))!;

    private static readonly Case[] All = AssistantEval.Cases.Concat(AssistantEval.Holdout).Concat(AssistantEval.Holdout2)
        .Concat(AssistantEval.Holdout3).Concat(AssistantEval.Holdout4).Concat(AssistantEval.Holdout5).ToArray();

    public static IEnumerable<object[]> Phrases() => All.Select(c => new object[] { c.Phrase });

    [Fact]
    public void TheSpikeSetHasAll200Phrases_AndEveryOneHasARecordedAnswer()
    {
        Assert.Equal(200, All.Length);
        Assert.Equal(200, All.Select(c => c.Phrase).Distinct().Count());
        Assert.All(All, c => Assert.True(Raw.ContainsKey(c.Phrase), "No recorded answer for: " + c.Phrase));
    }

    /// <summary>The recorded raw answer goes through the app's own path (guard, comma-list retry) and must end as the expected call.</summary>
    [Theory]
    [MemberData(nameof(Phrases))]
    public async Task Guard_TurnsTheRecordedAnswer_IntoTheExpectedCall(string phrase)
    {
        var c = All.Single(x => x.Phrase == phrase);
        var (_, final) = await AssistantResolver.ResolveAsync(phrase, p => Task.FromResult(Raw[p]));
        Assert.True(AssistantEval.Check(c, final) is null, $"{phrase}\n{AssistantEval.Check(c, final)}\ngot: {final}");
    }

    [Fact]
    public void Guard_LeavesUnparseableOutputAlone()
    {
        Assert.Equal("not json", AssistantGuard.Guard("buy milk", "not json"));
    }
}

public class DayTimeResolverTests
{
    // Thursday 2026-11-05 14:00.
    private static readonly DateTime Now = new(2026, 11, 5, 14, 0, 0);

    private static string Show(string? day, string? time)
    {
        var r = DayTimeResolver.Resolve(day, time, Now);
        return r.Date.ToString("yyyy-MM-dd") + (r.Time is { } t ? " " + t.ToString("HH:mm") : "");
    }

    [Theory]
    [InlineData("friday", null, "2026-11-06")]
    [InlineData("next tuesday", null, "2026-11-10")]
    [InlineData("next friday", null, "2026-11-13")]           // the one in the following week, not tomorrow
    [InlineData("tonight", null, "2026-11-05 20:00")]
    [InlineData(null, "6", "2026-11-05 18:00")]               // a bare hour 1 to 8 is PM; still ahead, so today
    [InlineData(null, "at 6", "2026-11-05 18:00")]
    [InlineData(null, "noon", "2026-11-06 12:00")]            // noon already passed today: tomorrow
    [InlineData(null, "9am", "2026-11-06 09:00")]
    [InlineData(null, "11pm", "2026-11-05 23:00")]
    [InlineData("15th", null, "2026-11-15")]
    [InlineData("4th", null, "2026-12-04")]                   // behind us this month: the next one
    [InlineData("31st", null, "2026-12-31")]                  // November has no 31st
    [InlineData("thursday", null, "2026-11-05")]              // today's weekday, no time: today
    [InlineData("thursday", "3pm", "2026-11-05 15:00")]       // time still ahead: today
    [InlineData("thursday", "1pm", "2026-11-12 13:00")]       // time already past: next week
    [InlineData("next thursday", null, "2026-11-12")]
    [InlineData("tomorrow", "9:30am", "2026-11-06 09:30")]
    [InlineData("tomorrow morning", null, "2026-11-06 09:00")]
    [InlineData("sunday", "5pm", "2026-11-08 17:00")]
    [InlineData("on the 20th", null, "2026-11-20")]
    [InlineData("morning", null, "2026-11-06 09:00")]         // 09:00 already passed: tomorrow
    [InlineData("evening", null, "2026-11-05 18:00")]
    [InlineData("friday", "17:00", "2026-11-06 17:00")]
    [InlineData(null, null, "2026-11-05")]                    // nothing said: today, all-day
    [InlineData("today", null, "2026-11-05")]
    [InlineData("next week", null, "2026-11-12")]
    public void Resolve_Table(string? day, string? time, string expected) => Assert.Equal(expected, Show(day, time));

    [Fact]
    public void Resolve_FlagsDayWordsItDoesNotUnderstand()
    {
        var r = DayTimeResolver.Resolve("someday", null, Now);
        Assert.False(r.DayRecognized);
        Assert.Equal(new DateOnly(2026, 11, 5), r.Date);
        Assert.True(DayTimeResolver.Resolve("friday", null, Now).DayRecognized);
    }

    [Theory]
    [InlineData("9", 9, 0)]
    [InlineData("5:30pm", 17, 30)]
    [InlineData("12am", 0, 0)]
    [InlineData("12pm", 12, 0)]
    [InlineData("17:00", 17, 0)]
    [InlineData("8", 20, 0)]
    [InlineData("midnight", 0, 0)]
    public void ParseTime_Table(string words, int hour, int minute) => Assert.Equal(new TimeOnly(hour, minute), DayTimeResolver.ParseTime(words));

    [Fact]
    public void ParseTime_RejectsNonsense()
    {
        Assert.Null(DayTimeResolver.ParseTime("later"));
        Assert.Null(DayTimeResolver.ParseTime("25"));
        Assert.Null(DayTimeResolver.ParseTime(null));
    }

    [Theory]
    [InlineData("5 minutes", "2026-11-05 14:05")]
    [InlineData("an hour", "2026-11-05 15:00")]
    [InlineData("1 hour", "2026-11-05 15:00")]
    [InlineData("20 min", "2026-11-05 14:20")]
    [InlineData("2 hours", "2026-11-05 16:00")]
    [InlineData("for 10 minutes", "2026-11-05 14:10")]
    [InlineData("half an hour", "2026-11-05 14:30")]
    public void Snooze_Table(string words, string until)
    {
        var s = DayTimeResolver.ParseSnooze(words, Now)!;
        Assert.False(s.Tomorrow);
        Assert.Equal(until, s.Until!.Value.ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public void Snooze_Tomorrow_AndNonsense()
    {
        Assert.True(DayTimeResolver.ParseSnooze("tomorrow", Now)!.Tomorrow);
        Assert.Null(DayTimeResolver.ParseSnooze("soon", Now));
        Assert.Null(DayTimeResolver.ParseSnooze("3 days", Now));
        Assert.Null(DayTimeResolver.ParseSnooze("99999999999 minutes", Now));   // no int overflow
        Assert.Null(DayTimeResolver.ParseSnooze(null, Now));
    }

    [Theory]
    [InlineData("friday", "2026-11-06", "2026-11-06")]
    [InlineData("today", "2026-11-05", "2026-11-05")]
    [InlineData("tomorrow", "2026-11-06", "2026-11-06")]
    [InlineData("thursday", "2026-11-05", "2026-11-05")]      // a question: today's weekday means today
    [InlineData("this week", "2026-11-05", "2026-11-08")]     // today to Sunday
    [InlineData("next week", "2026-11-09", "2026-11-15")]     // Monday to Sunday
    [InlineData("this weekend", "2026-11-07", "2026-11-08")]
    [InlineData("next friday", "2026-11-13", "2026-11-13")]
    public void Range_Table(string words, string from, string to)
    {
        var r = DayTimeResolver.ResolveRange(words, Now)!.Value;
        Assert.Equal((from, to), (r.From.ToString("yyyy-MM-dd"), r.To.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public void Range_IsNullForNonDays() => Assert.Null(DayTimeResolver.ResolveRange("someday", Now));
}

public class ReminderMatcherTests
{
    private static Reminder R(int id, string title, string date = "2026-11-06", bool done = false) =>
        new() { Id = id, Title = title, DueDate = DateOnly.Parse(date), IsCompleted = done };

    [Fact]
    public void OneMatch_ByWordsAndStems()
    {
        var list = new[] { R(1, "Buy milk"), R(2, "Stretching"), R(3, "Call grandma") };
        Assert.Equal(new[] { 1 }, ReminderMatcher.Match("the milk one", list).Select(r => r.Id));
        Assert.Equal(new[] { 2 }, ReminderMatcher.Match("stretch", list).Select(r => r.Id));       // stem: stretch / stretching
        Assert.Equal(new[] { 3 }, ReminderMatcher.Match("grandma's call", list).Select(r => r.Id)); // apostrophe, word order
    }

    [Fact]
    public void Plurals_Match() =>
        Assert.Equal(new[] { 1 }, ReminderMatcher.Match("cats", new[] { R(1, "Feed the cat") }).Select(r => r.Id));

    [Fact]
    public void NoMatch_IsEmpty_AndCompletedOnesAreIgnored()
    {
        Assert.Empty(ReminderMatcher.Match("dentist", new[] { R(1, "Buy milk") }));
        Assert.Empty(ReminderMatcher.Match("milk", new[] { R(1, "Buy milk", done: true) }));
        Assert.Empty(ReminderMatcher.Match("", new[] { R(1, "Buy milk") }));
    }

    [Fact]
    public void ATie_ListsEveryBestMatch_NearestDueDateFirst()
    {
        var list = new[] { R(1, "Dentist bill", "2026-11-20"), R(2, "Dentist", "2026-11-09"), R(3, "Buy milk") };
        Assert.Equal(new[] { 2, 1 }, ReminderMatcher.Match("dentist", list).Select(r => r.Id));
    }

    [Fact]
    public void MoreWordsMatched_BeatsFewer()
    {
        var list = new[] { R(1, "Dentist bill"), R(2, "Dentist") };
        Assert.Equal(new[] { 1 }, ReminderMatcher.Match("dentist bill", list).Select(r => r.Id));
    }
}

public class AssistantPlannerTests
{
    // Thursday 2026-11-05 14:00.
    private static (ReminderEnv E, AssistantPlanner P) Make()
    {
        var e = new ReminderEnv(new DateTime(2026, 11, 5, 14, 0, 0));
        return (e, new AssistantPlanner(e.Repo, e.Clock));
    }

    private static string Call(string tool, string args) => $$"""[{"name":"{{tool}}","arguments":{{args}}}]""";

    [Fact]
    public void Create_ShowsAConfirmCard_AndWritesNothingYet()
    {
        var (e, p) = Make();
        using var _ = e;
        var o = Assert.IsType<ConfirmOutcome>(p.Plan(Call("create_reminders",
            """{"items":[{"title":"call grandma","day":"sunday","time":"5pm","important":true}]}""")));
        var c = Assert.Single(o.Changes);
        Assert.Equal(ChangeKind.Create, c.Kind);
        Assert.Equal(new DateOnly(2026, 11, 8), c.After!.DueDate);
        Assert.Equal(new TimeOnly(17, 0), c.After.DueTime);
        Assert.True(c.After.IsImportant);
        Assert.Empty(e.Repo.ListAll());

        Assert.Single(p.Apply(o.Changes));
        var saved = Assert.Single(e.Repo.ListAll());
        Assert.Equal("call grandma", saved.Title);
        Assert.Equal(new DateOnly(2026, 11, 8), saved.DueDate);
    }

    [Fact]
    public void Create_WithoutATitle_IsDropped()
    {
        var (e, p) = Make();
        using var _ = e;
        var o = p.Plan(Call("create_reminders", """{"items":[{"title":"  ","day":"friday"},{"day":"friday"}]}"""));
        Assert.IsType<ReplyOutcome>(o);
        var two = Assert.IsType<ConfirmOutcome>(p.Plan(Call("create_reminders", """{"items":[{"day":"friday"},{"title":"buy milk"}]}""")));
        Assert.Equal("buy milk", Assert.Single(two.Changes).Title);
    }

    [Fact]
    public void EveryChange_NeedsAConfirm_AndNothingIsWrittenByPlanning()
    {
        var (e, p) = Make();
        using var _ = e;
        var milk = e.Repo.Create(ReminderEnv.R("Buy milk", "2026-11-06", "09:00"));
        var calls = new[]
        {
            Call("complete_reminder", """{"match":"milk"}"""),
            Call("delete_reminder", """{"match":"milk"}"""),
            Call("snooze_reminder", """{"match":"milk","snooze":"1 hour"}"""),
            Call("update_reminder", """{"match":"milk","title":"oat milk"}"""),
            Call("update_reminder", """{"match":"milk","day":"monday","time":"3pm"}"""),
        };
        foreach (var call in calls) Assert.IsType<ConfirmOutcome>(p.Plan(call));
        var after = e.Repo.Get(milk.Id)!;
        Assert.False(after.IsCompleted);
        Assert.Equal("Buy milk", after.Title);
        Assert.Null(after.SnoozedUntil);
    }

    [Fact]
    public void Delete_IsMarkedAsDelete_AndRemovesOnApply()
    {
        var (e, p) = Make();
        using var _ = e;
        var milk = e.Repo.Create(ReminderEnv.R("Buy milk", "2026-11-06"));
        var o = Assert.IsType<ConfirmOutcome>(p.Plan(Call("delete_reminder", """{"match":"milk"}""")));
        Assert.True(o.IsDelete);
        p.Apply(o.Changes);
        Assert.Null(e.Repo.Get(milk.Id));
    }

    [Fact]
    public void Complete_AndNotFound()
    {
        var (e, p) = Make();
        using var _ = e;
        var grandma = e.Repo.Create(ReminderEnv.R("Call grandma", "2026-11-08", "17:00"));
        p.Apply(Assert.IsType<ConfirmOutcome>(p.Plan(Call("complete_reminder", """{"match":"grandma"}"""))).Changes);
        Assert.True(e.Repo.Get(grandma.Id)!.IsCompleted);

        var none = Assert.IsType<ReplyOutcome>(p.Plan(Call("delete_reminder", """{"match":"milk"}""")));
        Assert.Contains("milk", none.Text);
        Assert.Contains("couldn't find", none.Text);
    }

    [Fact]
    public void Update_Rename_Move_AndImportant()
    {
        var (e, p) = Make();
        using var _ = e;
        var r = e.Repo.Create(ReminderEnv.R("Piano lesson", "2026-11-06", "16:00"));
        p.Apply(Assert.IsType<ConfirmOutcome>(p.Plan(Call("update_reminder", """{"match":"piano","day":"tuesday","time":"3pm","important":true}"""))).Changes);
        var moved = e.Repo.Get(r.Id)!;
        Assert.Equal(new DateOnly(2026, 11, 10), moved.DueDate);
        Assert.Equal(new TimeOnly(15, 0), moved.DueTime);
        Assert.True(moved.IsImportant);

        p.Apply(Assert.IsType<ConfirmOutcome>(p.Plan(Call("update_reminder", """{"match":"piano","title":"Guitar lesson"}"""))).Changes);
        var renamed = e.Repo.Get(r.Id)!;
        Assert.Equal("Guitar lesson", renamed.Title);
        Assert.Equal(new DateOnly(2026, 11, 10), renamed.DueDate);   // the rest is kept

        // A day alone keeps the time.
        p.Apply(Assert.IsType<ConfirmOutcome>(p.Plan(Call("update_reminder", """{"match":"guitar","day":"friday"}"""))).Changes);
        Assert.Equal((new DateOnly(2026, 11, 6), new TimeOnly(15, 0)), (e.Repo.Get(r.Id)!.DueDate, e.Repo.Get(r.Id)!.DueTime!.Value));

        Assert.IsType<ReplyOutcome>(p.Plan(Call("update_reminder", """{"match":"guitar"}""")));   // nothing to change: asks
    }

    [Fact]
    public void Tie_AsksWhichOne_AndWritesNothingUntilAChoiceAndAConfirm()
    {
        var (e, p) = Make();
        using var _ = e;
        var dentist = e.Repo.Create(ReminderEnv.R("Dentist", "2026-11-09", "10:00"));
        var bill = e.Repo.Create(ReminderEnv.R("Dentist bill", "2026-11-20"));
        var choose = Assert.IsType<ChooseOutcome>(p.Plan(Call("snooze_reminder", """{"match":"dentist","snooze":"1 hour"}""")));
        Assert.Equal(new[] { dentist.Id, bill.Id }, choose.Candidates.Select(r => r.Id));
        Assert.Null(e.Repo.Get(dentist.Id)!.SnoozedUntil);

        var confirm = Assert.IsType<ConfirmOutcome>(choose.Pick(choose.Candidates[0]));
        Assert.Null(e.Repo.Get(dentist.Id)!.SnoozedUntil);          // still nothing written: the card comes first
        p.Apply(confirm.Changes);
        Assert.Equal("2026-11-05T15:00", e.Repo.Get(dentist.Id)!.SnoozedUntil);
        Assert.Null(e.Repo.Get(bill.Id)!.SnoozedUntil);
    }

    [Fact]
    public void Tie_ShowsAtMostFourChoices()
    {
        var (e, p) = Make();
        using var _ = e;
        for (var i = 0; i < 6; i++) e.Repo.Create(ReminderEnv.R("Call mom " + i, "2026-11-0" + (6 + i % 3)));
        var choose = Assert.IsType<ChooseOutcome>(p.Plan(Call("complete_reminder", """{"match":"call mom"}""")));
        Assert.Equal(4, choose.Candidates.Count);
    }

    [Fact]
    public void Snooze_Tomorrow_MovesTheDate_LikeTheToastButton()
    {
        var (e, p) = Make();
        using var _ = e;
        var r = e.Repo.Create(ReminderEnv.R("Water plants", "2026-11-05", "18:00"));
        p.Apply(Assert.IsType<ConfirmOutcome>(p.Plan(Call("snooze_reminder", """{"match":"plants","snooze":"tomorrow"}"""))).Changes);
        var after = e.Repo.Get(r.Id)!;
        Assert.Equal(new DateOnly(2026, 11, 6), after.DueDate);
        Assert.Equal(new TimeOnly(18, 0), after.DueTime);
        Assert.Null(after.SnoozedUntil);

        // Never pulls a later reminder earlier.
        var later = e.Repo.Create(ReminderEnv.R("Later", "2026-11-20"));
        p.Apply(Assert.IsType<ConfirmOutcome>(p.Plan(Call("snooze_reminder", """{"match":"later","snooze":"tomorrow"}"""))).Changes);
        Assert.Equal(new DateOnly(2026, 11, 20), e.Repo.Get(later.Id)!.DueDate);
    }

    [Fact]
    public void Snooze_WithoutUsableWords_AsksHowLong()
    {
        var (e, p) = Make();
        using var _ = e;
        e.Repo.Create(ReminderEnv.R("Dentist", "2026-11-09"));
        Assert.IsType<ReplyOutcome>(p.Plan(Call("snooze_reminder", """{"match":"dentist"}""")));
        Assert.IsType<ReplyOutcome>(p.Plan(Call("snooze_reminder", """{"match":"dentist","snooze":"soon"}""")));
    }

    [Fact]
    public void Apply_ReportsAReminderThatDisappeared()
    {
        var (e, p) = Make();
        using var _ = e;
        var r = e.Repo.Create(ReminderEnv.R("Buy milk", "2026-11-06"));
        var o = Assert.IsType<ConfirmOutcome>(p.Plan(Call("complete_reminder", """{"match":"milk"}""")));
        e.Repo.Delete(r.Id);
        Assert.Contains("isn't there", Assert.Single(p.Apply(o.Changes)));
    }

    [Fact]
    public void Find_Overdue_Day_Range_Important_AndText()
    {
        var (e, p) = Make();
        using var _ = e;
        e.Repo.Create(ReminderEnv.R("Late thing", "2026-11-03"));
        e.Repo.Create(ReminderEnv.R("Done late", "2026-11-03", done: true));
        e.Repo.Create(ReminderEnv.R("Today", "2026-11-05", "18:00"));
        e.Repo.Create(ReminderEnv.R("Friday thing", "2026-11-06"));
        var imp = ReminderEnv.R("Saturday big", "2026-11-07"); imp.IsImportant = true; e.Repo.Create(imp);
        e.Repo.Create(ReminderEnv.R("Next week", "2026-11-10"));

        string[] Titles(string args) => Assert.IsType<FoundOutcome>(p.Plan(Call("find_reminders", args))).Reminders.Select(r => r.Title).ToArray();

        Assert.Equal(new[] { "Late thing" }, Titles("""{"overdue":true}"""));                                 // completed ones are left out
        Assert.Equal(new[] { "Friday thing" }, Titles("""{"day":"friday"}"""));
        Assert.Equal(new[] { "Today", "Friday thing", "Saturday big" }, Titles("""{"day":"this week"}"""));
        Assert.Equal(new[] { "Next week" }, Titles("""{"day":"next week"}"""));
        Assert.Equal(new[] { "Saturday big" }, Titles("""{"important":true}"""));
        Assert.Equal(new[] { "Saturday big" }, Titles("""{"day":"this week","important":true}"""));
        Assert.Equal(new[] { "Friday thing" }, Titles("""{"text":"friday"}"""));

        Assert.IsType<ReplyOutcome>(p.Plan(Call("find_reminders", """{"day":"saturday","text":"nothing here"}""")));   // nothing found
        Assert.IsType<ReplyOutcome>(p.Plan(Call("find_reminders", "{}")));                                           // asks what to find
        Assert.IsType<ReplyOutcome>(p.Plan(Call("find_reminders", """{"day":"someday"}""")));
    }

    [Fact]
    public void None_GarbageAndMissingArguments_AreAFriendlyReply()
    {
        var (e, p) = Make();
        using var _ = e;
        Assert.IsType<ReplyOutcome>(p.Plan(Call("none", "{}")));
        Assert.IsType<ReplyOutcome>(p.Plan("not json"));
        Assert.IsType<ReplyOutcome>(p.Plan("""[{"name":"create_reminders"}]"""));
        Assert.Empty(e.Repo.ListAll());
    }

    [Fact]
    public void Planner_OnlyEverSeesTheCurrentProfilesReminders()
    {
        var (e, p) = Make();
        using var _ = e;
        e.Repo.Create(ReminderEnv.R("Buy milk", "2026-11-06"));
        e.Session.SwitchTo(e.B);
        Assert.IsType<ReplyOutcome>(p.Plan(Call("complete_reminder", """{"match":"milk"}""")));
    }
}

public class GpuLogTests
{
    // Real lines captured from llama.cpp on the dev PC (RTX 4060 Laptop + AMD Radeon 780M, Vulkan, via Remindr.exe --assistant-check, 2026-11-06).
    private const string Nvidia = "ggml_vulkan: 0 = NVIDIA GeForce RTX 4060 Laptop GPU (NVIDIA) | uma: 0 | fp16: 1 | bf16: 1 | warp size: 32 | shared memory: 49152 | int dot: 1 | matrix cores: NV_coopmat2";
    private const string Amd = "ggml_vulkan: 1 = AMD Radeon 780M Graphics (AMD proprietary driver) | uma: 1 | fp16: 1 | bf16: 1 | warp size: 64 | shared memory: 32768 | int dot: 1 | matrix cores: KHR_coopmat";
    private const string Using = "llama_model_load_from_file_impl: using device Vulkan0 (NVIDIA GeForce RTX 4060 Laptop GPU) (0000:01:00.0) - 7188 MiB free";
    private const string Offloaded = "load_tensors: offloaded 17/17 layers to GPU";

    [Fact]
    public void DedicatedCard_IsTheOneWithItsOwnMemory_WhateverItsVulkanNumber()
    {
        var log = new GpuLog();
        log.Feed(Amd.Replace("ggml_vulkan: 1", "ggml_vulkan: 0"));     // the iGPU listed first
        log.Feed(Nvidia.Replace("ggml_vulkan: 0", "ggml_vulkan: 1"));
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU", log.DedicatedCard);
    }

    [Fact]
    public void OnlyAnIntegratedGpu_IsNotACard()
    {
        var log = new GpuLog();
        log.Feed(Amd);
        Assert.Null(log.DedicatedCard);
    }

    [Fact]
    public void FullyOn_NeedsTheDeviceLineAndAllLayers()
    {
        var log = new GpuLog();
        log.Feed(Nvidia);
        log.Feed(Amd);
        const string card = "NVIDIA GeForce RTX 4060 Laptop GPU";
        Assert.False(log.FullyOn(card));
        log.Feed(Using);
        log.Feed("load_tensors: offloaded 16/17 layers to GPU");
        Assert.False(log.FullyOn(card));
        log.Feed(Offloaded);
        Assert.True(log.FullyOn(card));
        log.ResetLoad();
        Assert.False(log.FullyOn(card));
        Assert.Equal(card, log.DedicatedCard);   // the device list survives a reload
    }
}

public class AssistantServiceTests
{
    private sealed class FakeModel : IAssistantModel
    {
        public bool Disposed;
        public string? Device => null;
        public string? NoteKey => null;
        public async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<string> InferAsync(string phrase, CancellationToken ct = default) =>
            Task.FromResult("""[{"name": "create_reminders", "arguments": {"items": [{"title": "buy milk"}]}}]""");
        public void Dispose() => Disposed = true;
    }

    private static (TempDb T, SettingsRepo S) Settings()
    {
        var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        return (t, new SettingsRepo(t.Db));
    }

    [Fact]
    public async Task IsOffByDefault_AndAMissingModelFileIsAFriendlyState_NotACrash()
    {
        var (t, s) = Settings();
        using var _ = t;
        var service = new AssistantService(s, _ => throw new InvalidOperationException("must not load"), Path.Combine(t.Dir, "missing.gguf"));
        Assert.False(service.Enabled);
        Assert.Equal(AssistantState.Off, service.State);
        service.Enabled = true;
        Assert.Equal(AssistantState.NotInstalled, service.State);
        Assert.Equal(AssistantState.NotInstalled, await service.EnsureReadyAsync());
        Assert.Null(await service.AskAsync("buy milk"));
    }

    [Fact]
    public async Task LoadsLazily_AnswersThroughTheGuard_AndTurningItOffFreesTheModel()
    {
        var (t, s) = Settings();
        using var _ = t;
        Directory.CreateDirectory(t.Dir);
        var file = Path.Combine(t.Dir, "model.gguf");
        File.WriteAllText(file, "x");
        var model = new FakeModel();
        var loads = 0;
        var service = new AssistantService(s, _ => { loads++; return model; }, file);
        service.Enabled = true;
        Assert.Equal(0, loads);                                       // nothing loads until first use
        var json = await service.AskAsync("buy milk");
        Assert.Equal(1, loads);
        Assert.Contains("create_reminders", json);
        Assert.Equal(AssistantState.Ready, service.State);
        await service.AskAsync("buy milk");
        Assert.Equal(1, loads);                                       // loaded once

        service.Enabled = false;
        Assert.True(SpinWait.SpinUntil(() => model.Disposed, 2000));   // freed off the UI thread
        Assert.Equal(AssistantState.Off, service.State);
        Assert.Equal("0", s.Get(AssistantService.EnabledKey));
    }

    [Fact]
    public async Task TheModelFileChangingDuringALoad_DropsTheOldModel_AndLoadsTheNewFile()
    {
        var (t, s) = Settings();
        using var _ = t;
        Directory.CreateDirectory(t.Dir);
        var oldFile = Path.Combine(t.Dir, "old.gguf");
        var newFile = Path.Combine(t.Dir, "new.gguf");
        File.WriteAllText(oldFile, "x");
        File.WriteAllText(newFile, "x");
        var path = oldFile;
        var gate = new ManualResetEventSlim();
        var loaded = new List<(string Path, FakeModel Model)>();
        var service = new AssistantService(s, (p, _) =>
        {
            if (p == oldFile) gate.Wait(5000);
            var m = new FakeModel();
            lock (loaded) loaded.Add((p, m));
            return m;
        }, () => path);
        service.Enabled = true;
        var first = service.EnsureReadyAsync();
        path = newFile;          // a tier switch finished while the old file was loading
        service.Unload();
        gate.Set();
        await first;
        for (var i = 0; i < 100 && service.State != AssistantState.Ready; i++) await Task.Delay(20);
        Assert.Equal(AssistantState.Ready, service.State);
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded[0].Model.Disposed);                        // the old file's model never became the live one
        Assert.Equal(newFile, loaded[1].Path);
        Assert.False(loaded[1].Model.Disposed);
    }

    [Fact]
    public async Task ALoadThatFails_IsAFailedState_NotAnException()
    {
        var (t, s) = Settings();
        using var _ = t;
        Directory.CreateDirectory(t.Dir);
        var file = Path.Combine(t.Dir, "model.gguf");
        File.WriteAllText(file, "x");
        var service = new AssistantService(s, _ => throw new InvalidOperationException("bad file"), file);
        service.Enabled = true;
        Assert.Equal(AssistantState.Failed, await service.EnsureReadyAsync());
        Assert.Equal("bad file", service.FailureMessage);
    }

    [Fact]
    public void DefaultModelsFolder_IsLocalAppData_OrInsideTheOverriddenDataFolder()
    {
        Assert.EndsWith(Path.Combine("Remindr", "models"), AppPaths.FromEnvironmentValue(null).ModelsDir);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.FromEnvironmentValue(null).ModelsDir);
        var custom = Path.Combine(Path.GetTempPath(), "remindr-custom");
        Assert.Equal(Path.Combine(custom, "models"), AppPaths.FromEnvironmentValue(custom).ModelsDir);
    }
}
