using System.Globalization;
using Remindr.Data.Repositories;

namespace Remindr.Services;

public enum ToastAction { Open, Complete, Snooze, Dismiss }

/// <summary>The snooze list inside a toast.</summary>
public enum SnoozeChoice { Minutes5, Hour1, Tomorrow }

/// <summary>What a toast click carries: the action, the reminder, its profile and the occurrence key. Pure text round trip.</summary>
public sealed record ToastArgs(ToastAction Action, int ReminderId, int ProfileId, string Occurrence)
{
    /// <summary>Id of the toast's snooze selection box.</summary>
    public const string SnoozeInputId = "snooze";

    public string Encode() =>
        $"a={Action.ToString().ToLowerInvariant()}&r={ReminderId.ToString(CultureInfo.InvariantCulture)}" +
        $"&p={ProfileId.ToString(CultureInfo.InvariantCulture)}&o={Uri.EscapeDataString(Occurrence)}";

    public ToastArgs With(ToastAction action) => this with { Action = action };

    /// <summary>Notification tag: one per reminder, so a new alert replaces the old one.</summary>
    public string Tag => TagFor(ProfileId, ReminderId);
    /// <summary>Notification group: the occurrence, so an alert for a moved reminder is a different toast.</summary>
    public string Group => Occurrence;

    public static string TagFor(int profileId, int reminderId) =>
        $"p{profileId.ToString(CultureInfo.InvariantCulture)}r{reminderId.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? text, out ToastArgs args)
    {
        args = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) return false;
            map[part[..eq]] = Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        if (!map.TryGetValue("a", out var a) || !Enum.TryParse<ToastAction>(a, true, out var action) || !Enum.IsDefined(action)) return false;
        if (!map.TryGetValue("r", out var r) || !int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out var rid)) return false;
        if (!map.TryGetValue("p", out var p) || !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) return false;
        if (!map.TryGetValue("o", out var o)) return false;
        args = new ToastArgs(action, rid, pid, o);
        return true;
    }
}

public sealed record ToastOutcome(ToastAction Action, bool Found)
{
    /// <summary>True when the app window should come forward (a body click, not a button).</summary>
    public bool ShowWindow => Action == ToastAction.Open;
}

/// <summary>Turns a toast click into its effect on the data. No UI.</summary>
public sealed class ToastActionHandler
{
    private readonly ReminderRepo _reminders;
    private readonly NotificationLogRepo _log;
    private readonly IClock _clock;

    public ToastActionHandler(ReminderRepo reminders, NotificationLogRepo log, IClock clock)
    {
        _reminders = reminders;
        _log = log;
        _clock = clock;
    }

    public static SnoozeChoice ParseChoice(string? id) => id switch
    {
        "1h" => SnoozeChoice.Hour1,
        "tomorrow" => SnoozeChoice.Tomorrow,
        _ => SnoozeChoice.Minutes5
    };

    public static string ChoiceId(SnoozeChoice c) => c switch
    {
        SnoozeChoice.Hour1 => "1h",
        SnoozeChoice.Tomorrow => "tomorrow",
        _ => "5m"
    };

    /// <summary>Local time a timed snooze ends: now plus the span, rounded up to the next whole minute (stored at minute precision).</summary>
    public static DateTime SnoozeUntil(SnoozeChoice choice, DateTime now)
    {
        var span = choice == SnoozeChoice.Hour1 ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(5);
        var t = now + span;
        var floor = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
        return t > floor ? floor.AddMinutes(1) : floor;
    }

    /// <param name="snoozeChoiceId">The selection box value that came with a Snooze click.</param>
    public ToastOutcome Handle(ToastArgs args, string? snoozeChoiceId = null)
    {
        var r = _reminders.Get(args.ReminderId, args.ProfileId);
        if (r is null) return new ToastOutcome(args.Action, false);
        // A stale toast of a reminder completed meanwhile (in the app or another toast) changes nothing.
        if (r.IsCompleted) return new ToastOutcome(args.Action, true);

        switch (args.Action)
        {
            case ToastAction.Complete:
                _reminders.SetCompleted(args.ReminderId, true, args.ProfileId);
                break;
            case ToastAction.Snooze:
                var choice = ParseChoice(snoozeChoiceId);
                if (choice == SnoozeChoice.Tomorrow)
                {
                    // Keeps the time and clears the snooze. Never pulls a later reminder earlier (e.g. a test toast).
                    var tomorrow = _clock.Today.AddDays(1);
                    if (r.DueDate < tomorrow) _reminders.MoveToDate(args.ReminderId, tomorrow, args.ProfileId);
                }
                else
                    _reminders.SetSnoozedUntil(args.ReminderId, SnoozeUntil(choice, _clock.Now), args.ProfileId);
                break;
            case ToastAction.Dismiss:
                _log.Insert(new NotificationLogEntry(args.ReminderId, args.Occurrence, NotificationKind.Dismissed, _clock.UtcNow));
                break;
        }
        return new ToastOutcome(args.Action, true);
    }
}
