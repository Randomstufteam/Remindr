using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Remindr.Services.Assistant;

/// <summary>
/// The app's path from a phrase to a guarded call (ported from the spike's Resolve): one model call, the guard, and for a comma list the
/// model missed items from, one model call per part. <paramref name="infer"/> is the model, or recorded answers in tests.
/// </summary>
public static class AssistantResolver
{
    public static async Task<(string Raw, string Final)> ResolveAsync(string phrase, Func<string, Task<string>> infer)
    {
        var raw = await infer(phrase);
        var guarded = AssistantGuard.Guard(phrase, raw);
        var parts = AssistantGuard.Segments(phrase);
        if (parts.Count < 2 || AssistantGuard.ItemCount(guarded) is not { } have || have >= parts.Count) return (raw, guarded);
        var items = new JsonArray();
        foreach (var part in parts)
            if (AssistantGuard.Items(AssistantGuard.Guard(part, await infer(part))) is { } got) foreach (var i in got) items.Add(i!.DeepClone());
        foreach (var what in AssistantGuard.MarkedImportant(phrase))
            foreach (var i in items.OfType<JsonObject>())
                if (i["title"]?.ToString() is { } t && Regex.IsMatch(t.ToLowerInvariant(), $@"\b{Regex.Escape(what)}")) i["important"] = true;
        return (raw, """[{"name":"create_reminders","arguments":{"items":""" + items.ToJsonString() + "}}]");
    }
}
