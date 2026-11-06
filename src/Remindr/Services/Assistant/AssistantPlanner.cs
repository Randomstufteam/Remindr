using System.Text.Json;
using System.Text.Json.Nodes;
using Remindr.Data.Repositories;
using Remindr.Models;

namespace Remindr.Services.Assistant;

public enum ChangeKind { Create, Complete, Delete, Snooze, Update }

/// <summary>
/// One proposed change, shown on the confirm card and written only after Confirm. <see cref="After"/> is the reminder to create
/// (Create) or the edited copy (Update); <see cref="Snooze"/> is set for Snooze.
/// </summary>
public sealed record PlannedChange(ChangeKind Kind, Reminder? Target, Reminder? After = null, ResolvedSnooze? Snooze = null)
{
    public string Title => (After ?? Target)!.Title;
}

/// <summary>What the assistant does with a message. The planner never writes; only <see cref="AssistantPlanner.Apply"/> does, after Confirm.</summary>
public abstract record AssistantOutcome;
/// <summary>A plain sentence: an answer, "I couldn't find...", or a question back.</summary>
public sealed record ReplyOutcome(string Text) : AssistantOutcome;
/// <summary>A confirm card listing every change. Nothing is written until the user confirms.</summary>
public sealed record ConfirmOutcome(IReadOnlyList<PlannedChange> Changes) : AssistantOutcome
{
    public bool IsDelete => Changes.Any(c => c.Kind == ChangeKind.Delete);
}
/// <summary>Several reminders fit equally well: the user picks one, and <see cref="Pick"/> continues to the confirm card.</summary>
public sealed record ChooseOutcome(string Prompt, IReadOnlyList<Reminder> Candidates, Func<Reminder, AssistantOutcome> Pick) : AssistantOutcome;
/// <summary>The answer to a question: the matching reminders, shown as cards.</summary>
public sealed record FoundOutcome(string Heading, IReadOnlyList<Reminder> Reminders) : AssistantOutcome;

/// <summary>
/// Turns the guarded call (JSON text) into what to show: a reply, a confirm card, a "which one?" choice or a list of results.
/// Dates are resolved here by <see cref="DayTimeResolver"/>, matches by <see cref="ReminderMatcher"/>. Reads through the repo but never
/// writes until <see cref="Apply"/>. UI-free.
/// </summary>
public sealed class AssistantPlanner
{
    /// <summary>The most choices shown for a tie.</summary>
    public const int MaxChoices = 4;

    private readonly ReminderRepo _repo;
    private readonly IClock _clock;

    public AssistantPlanner(ReminderRepo repo, IClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public AssistantOutcome Plan(string guardedJson)
    {
        JsonObject? call, args;
        try
        {
            var node = JsonNode.Parse(guardedJson);
            call = (node is JsonArray { Count: > 0 } a ? a[0] : node) as JsonObject;
            args = call?["arguments"] as JsonObject;
            _ = args?.Count;
        }
        catch (Exception e) when (e is JsonException or ArgumentException) { return None(); }
        if (call is null || args is null) return None();

        return AssistantGuard.Str(call, "name") switch
        {
            "create_reminders" => Create(args),
            "complete_reminder" => Target(args, m => Confirm(ChangeKind.Complete, m)),
            "delete_reminder" => Target(args, m => Confirm(ChangeKind.Delete, m)),
            "snooze_reminder" => Snooze(args),
            "update_reminder" => Update(args),
            "find_reminders" => Find(args),
            _ => None(),
        };
    }

    static AssistantOutcome None() => new ReplyOutcome(Loc.Get("Assistant_NoneReply"));
    static string? S(JsonObject o, string k) => AssistantGuard.Str(o, k) is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    static bool B(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue(out bool b) && b;
    static ConfirmOutcome Confirm(ChangeKind kind, Reminder target) => new(new[] { new PlannedChange(kind, target) });

    AssistantOutcome Create(JsonObject args)
    {
        var now = _clock.Now;
        var changes = new List<PlannedChange>();
        foreach (var item in (args["items"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            // A create without a title is dropped.
            if (S(item, "title") is not { } title) continue;
            var when = DayTimeResolver.Resolve(S(item, "day"), S(item, "time"), now);
            changes.Add(new PlannedChange(ChangeKind.Create, null,
                new Reminder { Title = title, DueDate = when.Date, DueTime = when.Time, IsImportant = B(item, "important") }));
        }
        return changes.Count == 0 ? new ReplyOutcome(Loc.Get("Assistant_NoTitle")) : new ConfirmOutcome(changes);
    }

    /// <summary>Finds the reminder named by <c>match</c>: none is a reply, several a choice, one goes on to <paramref name="then"/>.</summary>
    AssistantOutcome Target(JsonObject args, Func<Reminder, AssistantOutcome> then)
    {
        var words = S(args, "match");
        var found = ReminderMatcher.Match(words, _repo.ListAll());
        if (found.Count == 0) return new ReplyOutcome(Loc.Format("Assistant_NotFound", words ?? ""));
        if (found.Count == 1) return then(found[0]);
        return new ChooseOutcome(Loc.Get("Assistant_WhichOne"), found.Take(MaxChoices).ToList(), then);
    }

    AssistantOutcome Snooze(JsonObject args)
    {
        var match = S(args, "match") ?? "";
        if (DayTimeResolver.ParseSnooze(S(args, "snooze"), _clock.Now) is not { } snooze)
            return new ReplyOutcome(Loc.Format("Assistant_SnoozeHowLong", match));
        return Target(args, m => new ConfirmOutcome(new[] { new PlannedChange(ChangeKind.Snooze, m, null, snooze) }));
    }

    AssistantOutcome Update(JsonObject args)
    {
        var match = S(args, "match") ?? "";
        var title = S(args, "title");
        var day = S(args, "day");
        var time = S(args, "time");
        var important = B(args, "important");
        if (title is null && day is null && time is null && !important)
            return new ReplyOutcome(Loc.Format("Assistant_NoChange", match));
        var now = _clock.Now;
        var when = day is null ? null : DayTimeResolver.Resolve(day, time, now);
        if (when is { DayRecognized: false } && title is null && time is null && !important)
            return new ReplyOutcome(Loc.Format("Assistant_DayUnknown", day!));

        return Target(args, m =>
        {
            var after = m.Clone();
            if (title is not null) after.Title = title;
            // A day alone keeps the reminder's time; a time alone keeps its day.
            if (when is { DayRecognized: true }) after.DueDate = when.Date;
            if (DayTimeResolver.ParseTime(time) is { } t) after.DueTime = t;
            if (important) after.IsImportant = true;
            return new ConfirmOutcome(new[] { new PlannedChange(ChangeKind.Update, m, after) });
        });
    }

    AssistantOutcome Find(JsonObject args)
    {
        var now = _clock.Now;
        var day = S(args, "day");
        var text = S(args, "text");
        var overdue = B(args, "overdue");
        var important = B(args, "important");
        if (!overdue && !important && day is null && text is null) return new ReplyOutcome(Loc.Get("Assistant_FindAsk"));

        IEnumerable<Reminder> list;
        if (overdue) list = _repo.ListOverdue(now);
        else if (day is not null)
        {
            if (DayTimeResolver.ResolveRange(day, now) is not { } range) return new ReplyOutcome(Loc.Format("Assistant_DayUnknown", day));
            list = _repo.ListByRange(range.From, range.To);
        }
        else list = _repo.ListAll();

        list = list.Where(r => !r.IsCompleted);
        if (important) list = list.Where(r => r.IsImportant);
        if (text is not null) list = ReminderMatcher.Rank(text, list).Where(x => x.Score >= 0.5).Select(x => x.Reminder);

        var result = list.ToList();
        if (result.Count == 0) return new ReplyOutcome(Loc.Get("Assistant_FindNone"));
        return new FoundOutcome(Loc.Plural("Assistant_Found", result.Count), result);
    }

    /// <summary>
    /// Writes a confirmed card through the repo (so Today, the scheduler and the tray refresh as they do for any change) and returns one
    /// result line per change. A reminder that disappeared meanwhile gets a "isn't there anymore" line instead.
    /// </summary>
    public IReadOnlyList<string> Apply(IReadOnlyList<PlannedChange> changes)
    {
        var lines = new List<string>();
        foreach (var c in changes)
        {
            var ok = c.Kind switch
            {
                ChangeKind.Create => _repo.Create(c.After!) is not null,
                ChangeKind.Complete => _repo.SetCompleted(c.Target!.Id, true),
                ChangeKind.Delete => _repo.Delete(c.Target!.Id),
                ChangeKind.Update => _repo.Update(c.After!),
                ChangeKind.Snooze => ApplySnooze(c),
                _ => false,
            };
            lines.Add(ok ? Loc.Format("Assistant_Done_" + c.Kind, c.Title) : Loc.Format("Assistant_Gone", c.Title));
        }
        return lines;
    }

    bool ApplySnooze(PlannedChange c)
    {
        if (_repo.Get(c.Target!.Id) is not { } fresh) return false;
        if (c.Snooze!.Tomorrow) { ToastActionHandler.SnoozeToTomorrow(_repo, fresh, _clock.Today); return true; }
        return _repo.SetSnoozedUntil(fresh.Id, c.Snooze.Until);
    }
}
