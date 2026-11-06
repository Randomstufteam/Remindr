using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Remindr.Data.Repositories;

namespace Remindr.Services.Assistant;

/// <summary>Work changes reminders through the guarded tool pipeline (with a confirm card); Chat is a plain conversation with no tools.</summary>
public enum AssistantMode { Work, Chat }

/// <summary>The Work / Chat switch: the per-profile setting and the routing rules. UI-free.</summary>
public static class AssistantModes
{
    public const string Key = "assistant_mode";
    /// <summary>Earlier messages kept as context in Chat (about six exchanges).</summary>
    public const int ChatHistoryLimit = 12;

    public static AssistantMode Read(SettingsRepo settings, int profileId) =>
        settings.Get(Key, profileId) == "chat" ? AssistantMode.Chat : AssistantMode.Work;

    public static void Write(SettingsRepo settings, int profileId, AssistantMode mode) =>
        settings.Set(Key, mode == AssistantMode.Chat ? "chat" : "work", profileId);

    // Verbs and question starters that open a request about reminders. Only a cheap hint: the Work pipeline (model + guard) then decides.
    static readonly Regex ActionHint = new(
        @"^\s*(?:please\s+|can you\s+|could you\s+)?(?:remind|remember|add|set|create|make|schedule|delete|remove|cancel|snooze|postpone|mark|complete|finish|move|reschedule|rename|change|update|push|put|list|show|find|what'?s\s+(?:on|due|overdue|planned|coming)|what\s+(?:do\s+i|have\s+i)|when\s+is|do\s+i\s+have|i\s+(?:just\s+)?(?:did|finished|completed|watered|paid|called|bought|sent)|dont\s+forget|don't\s+forget)\b|\b(?:reminders?|overdue|snooze)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when the message may ask for a change or a question about reminders (so Chat should check it with the Work pipeline).</summary>
    public static bool LooksLikeAction(string phrase) => ActionHint.IsMatch(phrase);

    /// <summary>The name of the first tool in a guarded call, or null when it isn't a parseable call.</summary>
    public static string? ToolOf(string guardedJson)
    {
        try
        {
            var node = JsonNode.Parse(guardedJson);
            var call = (node is JsonArray { Count: > 0 } a ? a[0] : node) as JsonObject;
            return call?["name"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>A guarded call that is a real action (any tool but <c>none</c>).</summary>
    public static bool IsAction(string? guardedJson) => guardedJson is not null && ToolOf(guardedJson) is { } t && t != "none";

    /// <summary>The last <see cref="ChatHistoryLimit"/> messages.</summary>
    public static IReadOnlyList<ChatTurn> Recent(IReadOnlyList<ChatTurn> history) =>
        history.Count <= ChatHistoryLimit ? history : history.Skip(history.Count - ChatHistoryLimit).ToList();
}
