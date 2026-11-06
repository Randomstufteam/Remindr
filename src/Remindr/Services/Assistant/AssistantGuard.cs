using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Remindr.Services.Assistant;

/// <summary>
/// The word rules that run on the model's answer before the app trusts it (ported from tools/Remindr.AiSpike, 100% on its 200 phrases):
/// a delete needs a delete word, a change needs a change word plus a target, a search needs a question, "important" comes from the user's
/// own words, a day or time the user never said is dropped, and comma lists are retried one part per model call. UI-free; the model's
/// output is JSON text in and JSON text out, so tests replay recorded answers with no model.
/// </summary>
public static class AssistantGuard
{
    static readonly Regex Marker =new(@"^(?:(?:it'?s|this is|thats|that is)\s+)?important!?$|^(?<what>.+?)\s+is\s+important!?$");
    /// <summary>The separate things in a comma list ("a, b and c"), not counting "it's important" / "rent is important" markers.</summary>
    public static List<string> Segments(string phrase)
    {
        if (!phrase.Contains(','))
        {
            var halves = Regex.Split(phrase, @"\s+and\s+").Select(p => p.Trim()).ToList();
            var own = halves.Count > 1 && halves.All(h => DayWords.IsMatch(Norm(h)) || TimeWords.IsMatch(Norm(h)));
            return own ? halves : new() { phrase };
        }
        return phrase.Split(',', ';').SelectMany(p => Regex.Split(p, @"\s+and\s+")).Select(p => p.Trim()).Where(p => p.Length > 0 && !Marker.IsMatch(Norm(p))).ToList();
    }
    /// <summary>Words marked important by a "rent is important" part of the phrase.</summary>
    public static IEnumerable<string> MarkedImportant(string phrase) =>
        phrase.Split(',', ';').Select(p => Marker.Match(Norm(p.Trim()))).Where(m => m.Success && m.Groups["what"].Success).Select(m => m.Groups["what"].Value);

    static readonly Regex DayWords = new(@"\b(?:the\s+)?(?<d>today|tonight|tomorrow|this week|next week|this weekend|(?:next |this )?(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)|\d{1,2}(?:st|nd|rd|th)|(?<!good\s)(?:morning|afternoon|evening))\b");
    static readonly Regex TimeWords = new(@"\b(?:\d{1,2}:\d{2}\s*(?:am|pm)?|\d{1,2}\s*(?:am|pm)|noon|midday|midnight)\b|(?<=\bat\s)\d{1,2}\b");

    /// <summary>
    /// What the app would do before trusting the model: drop a day, time or "important" the user never said, and turn a "new reminder"
    /// whose title shares no word with the message into "none". Returns the output unchanged when it isn't a parseable call.
    /// </summary>
    public static string Guard(string phrase, string output)
    {
        JsonNode? node;
        // A duplicate key ("items" twice) throws ArgumentException, and only on first access to the object, not in Parse.
        JsonObject? args;
        try { node = JsonNode.Parse(output); args = (node is JsonArray { Count: > 0 } a ? a[0] : node)?["arguments"] as JsonObject; _ = args?.Count; }
        catch (Exception e) when (e is JsonException or ArgumentException) { return output; }
        var call = node is JsonArray { Count: > 0 } a2 ? a2[0] : node;
        if (args is null) return output;
        var said = Norm(phrase);
        var wordList = Regex.Split(said, @"[^\p{L}\p{N}:]+").Where(w => w.Length > 0).ToList();
        var words = wordList.SelectMany(w => w.Split(':')).ToHashSet();
        bool Says(params string[] ws) => ws.Any(w => Regex.IsMatch(said, $@"\b{w}\b"));

        // Deleting loses data, so it needs a delete word; otherwise the user finished it ("I called grandma").
        var name = Str(call!.AsObject(), "name");
        if (name == "delete_reminder" && !Says("delete", "remove", "cancel", "erase", "drop", "get rid of"))
            call["name"] = name = "complete_reminder";
        // A change needs a change word; "dentist tomorrow at 10:30" is a new reminder.
        if (name == "update_reminder" && (!Says("move", "change", "rename", "make", "reschedule", "set", "switch", "update", "edit")
            || !(Regex.IsMatch(said, @"\bto\b") || Says("important", "rename"))))
        {
            var item = new JsonObject { ["title"] = Str(args, "match") };
            foreach (var k in new[] { "day", "time", "important" }) if (args[k] is { } v) item[k] = v.DeepClone();
            call["name"] = name = "create_reminders";
            call["arguments"] = args = new JsonObject { ["items"] = new JsonArray(item) };
        }
        if (name == "find_reminders" && Says("overdue", "miss", "missed", "late", "forgot", "forgotten"))
        {
            args["overdue"] = true;
            if (Str(args, "text") is { } t && Regex.IsMatch(Norm(t), @"^(overdue|miss|missed|late|forgot|forgotten)$")) args.Remove("text");
        }
        var dayWord = DayWords.Match(said) is { Success: true } dm ? dm.Groups["d"].Value : null;
        var times = TimeWords.Matches(said).Select(m => m.Value).Distinct().ToList();
        var timeWord = times.Count == 1 ? times[0] : null;
        var asks = Regex.IsMatch(said, @"^(tell me|sing|what|whats|how|who|why|can you|could you|good (morning|night|evening)|hi|hello|hey|thanks|thank you|open|launch|play|turn on|turn off|search|google)\b");
        var question = phrase.TrimEnd().EndsWith('?') || Regex.IsMatch(said, @"^(what|whats|when|whens|where|wheres|which|who|whos|show|list|any|anything|do i|did i|is there|are there|have i|how many)\b");
        var addWord = Says("remind", "reminder", "add", "remember", "forget", "dont forget");
        static JsonObject NewItems(JsonObject item) => new() { ["items"] = new JsonArray(item) };

        var single = Segments(phrase).Count == 1;
        void Become(string tool, JsonObject a) { call["name"] = name = tool; call["arguments"] = args = a; }
        string Thing(string g) => Regex.Replace(g.Trim(), @"^(?:the|my|a|an)\s+|\s+(?:one|reminder)$", "");
        if (single && Regex.Match(said, @"^rename\s+(?<old>.+?)\s+(?:to|as)\s+(?<new>.+)$") is { Success: true } rn)
            Become("update_reminder", new() { ["match"] = Thing(rn.Groups["old"].Value), ["title"] = rn.Groups["new"].Value.Trim() });
        else if (single && Regex.Match(said, @"^make\s+(?<what>.+?)\s+important$") is { Success: true } mk)
            Become("update_reminder", new() { ["match"] = Thing(mk.Groups["what"].Value), ["important"] = true });
        else if (single && Regex.Match(said, @"^(?:move|change|reschedule)\s+(?<what>.+?)\s+to\s+(?<to>.+)$") is { Success: true } mv
                 && (DayWords.IsMatch(mv.Groups["to"].Value) || TimeWords.IsMatch(mv.Groups["to"].Value)))
            Become("update_reminder", new() { ["match"] = Thing(mv.Groups["what"].Value) });
        else if (single && dayWord is null && timeWord is null && Regex.Match(said, @"^(?:delete|remove|cancel|erase|get rid of)\s+(?<what>.+)$") is { Success: true } del)
            Become("delete_reminder", new() { ["match"] = Thing(del.Groups["what"].Value) });
        else if (single && dayWord is null && timeWord is null
                 && Regex.Match(said, @"^(?:i (?!need|want|have|must|should|will|gotta|got|am|m\b|wanna|would|could|can)\w+|finished|done with|completed|mark)\s+(?:the |my )?(?<rest>.+?)(?:\s+(?:as\s+)?(?:done|complete|completed|finished))?$") is { Success: true } dn)
            Become("complete_reminder", new() { ["match"] = Thing(dn.Groups["rest"].Value) });
        else if (single && Regex.Match(said, @"^remind me about\s+(?:the\s+)?(?<what>.+?)\s+again\s+(?:(?:in|for|until|till|to)\s+)?(?<when>.+)$") is { Success: true } ag)
            Become("snooze_reminder", new() { ["match"] = Thing(ag.Groups["what"].Value), ["snooze"] = ag.Groups["when"].Value });
        // The model called it chat, but it has a day, time or "!" and isn't a question: something to add.
        else if (name == "none" && !question && !asks && ((dayWord ?? timeWord) is not null || phrase.Contains('!')) && Lead(said) is { } nl)
            Become("create_reminders", NewItems(new() { ["title"] = nl }));
        // A search that isn't a question ("call the dentist to reschedule") is something to add.
        else if (name == "find_reminders" && !question && !asks && Lead(said) is { } fl)
            Become("create_reminders", NewItems(new() { ["title"] = fl }));

        // "Done" needs a done word; "dentist tomorrow at 10:30" is a new reminder.
        if (name == "complete_reminder" && !Says("done", "finished", "finish", "complete", "completed", "tick", "check off", "checked off", "mark", "did") && !Regex.IsMatch(said, @"^i\b")
            && (dayWord ?? timeWord) is not null)
        {
            call["name"] = name = "create_reminders";
            call["arguments"] = args = NewItems(new() { ["title"] = Str(args, "match") });
        }
        // "I fed the cat" / "finished the grocery shopping" report something done, even when the model made a new reminder of it.
        if (name == "create_reminders" && dayWord is null && timeWord is null
            && Regex.Match(said, @"^(?:i (?!need|want|have|must|should|will|gotta|got|am|m\b|wanna|would|could)\w+|finished|done with|completed)\s+(?:the |my )?(?<rest>.+)$") is { Success: true } doneM)
        {
            call["name"] = name = "complete_reminder";
            call["arguments"] = args = new() { ["match"] = doneM.Groups["rest"].Value };
        }
        // A search needs a question; "vet appointment for the dog wednesday 11am" is a new reminder.
        if (name == "find_reminders" && !question && (dayWord ?? timeWord) is not null)
        {
            call["name"] = name = "create_reminders";
            call["arguments"] = args = NewItems(new() { ["title"] = Str(args, "text") ?? phrase });
        }
        // A question with no action word is a search: "show my important reminders" isn't a new reminder or a completion.
        if (question && name is "create_reminders" or "complete_reminder" && !addWord && !Says("done", "finished", "complete", "mark", "tick"))
        { call["name"] = name = "find_reminders"; call["arguments"] = args = new(); }
        if (name != "snooze_reminder" && (Says("again") || said.StartsWith("snooze"))
            && Regex.Match(said, @"^(?:remind me about|snooze)\s+(?:the\s+)?(?<what>.+?)\s+(?:reminder\s+)?(?:again\s+)?(?:in|for|until|till|to)\s+(?<when>.+)$") is { Success: true } snM)
        {
            call["name"] = name = "snooze_reminder";
            call["arguments"] = args = new() { ["match"] = snM.Groups["what"].Value, ["snooze"] = snM.Groups["when"].Value };
        }
        // Snoozing needs a "later" word; "what's the weather like" isn't about a reminder.
        if (name == "snooze_reminder")
        {
            if (!Says("snooze", "again", "later", "push", "postpone", "delay", "put off") && !Regex.IsMatch(said, @"\b(in|for) (\d+|an?|one)\b"))
            { call["name"] = name = "none"; call["arguments"] = args = new(); }
            else if (Says("tomorrow") && !(Str(args, "snooze") is { } sv && Norm(sv).Contains("tomorrow")))
                args["snooze"] = "tomorrow";
        }
        // A question about reminders that the model called chat.
        if (name == "none" && Says("reminders?", "overdue", "due", "planned", "schedule", "agenda", "anything"))
        { call["name"] = name = "find_reminders"; call["arguments"] = args = new(); }
        if (name == "find_reminders")
        {
            if (Says("important")) args["important"] = true;
            if (dayWord is not null && (Str(args, "day") is not { } fd || !said.Contains(Norm(fd)))) args["day"] = dayWord;
            if (args["day"] is not null && Str(args, "text") is { } ft && Regex.IsMatch(Norm(ft), @"^(due|planned|anything|happening|on)$")) args.Remove("text");
            // "when is the dentist": with nothing else to go on, search for the thing named.
            if (args["text"] is null && args["day"] is null && args["important"] is null && args["overdue"] is null
                && Regex.Replace(said, @"\b(when|where|what|whats|is|are|was|the|my|a|an|do|did|i|have|any|show|me|list|find|reminders?|for|about|due|planned|scheduled)\b|[^\p{L}\p{N}\s]", " ").Trim() is { Length: > 0 } rest)
                args["text"] = Regex.Replace(rest, @"\s+", " ");
        }
        if (name == "find_reminders" && dayWord is null && !Says("reminders?", "overdue", "due", "planned", "schedule", "agenda", "anything", "important", "coming up", "miss", "missed", "have")
            && !Regex.IsMatch(said, @"^(when is|whens|when do|when am|when are|where is)\b"))
        { call["name"] = name = "none"; call["arguments"] = args = new(); }
        // Chat or a question with no reminder words and no day or time is not something to add ("tell me a joke").
        if (name == "create_reminders" && asks && !addWord && dayWord is null && timeWord is null)
        { call["name"] = name = "none"; call["arguments"] = args = new(); }
        if (name == "update_reminder")
        {
            var newTitle = Str(args, "title");
            // "make the report important": the model sometimes names the change instead of the reminder.
            if (Str(args, "match") is { } mt && Regex.IsMatch(Norm(mt), @"^(important|it|one|reminder|that|this)$") && newTitle is not null) args["match"] = newTitle;
            if (newTitle is not null && !Says("rename", "call it", "name it", "retitle")) args.Remove("title");
            if (Says("rename", "call it", "name it", "retitle")) { args.Remove("day"); args.Remove("time"); }
            else
            {
                if (args["day"] is null && dayWord is not null) args["day"] = dayWord;
                if (args["time"] is null && timeWord is not null) args["time"] = timeWord;
            }
            if (Says("important")) args["important"] = true;
        }
        // One thing to add: take a day or time the model left out from the phrase itself, and keep the user's own lead words in the title.
        if (name == "create_reminders" && args["items"] is JsonArray { Count: 1 } one && one[0] is JsonObject only)
        {
            if (dayWord is not null && (Str(only, "day") is not { } d1 || !said.Contains(Norm(d1)))) only["day"] = dayWord;
            if (only["time"] is null && timeWord is not null) only["time"] = timeWord;
            if (Lead(said) is { } lead && !(Str(only, "title") is { } tt && Norm(tt).Contains(lead.Split(' ')[0]))) only["title"] = lead;
        }

        var hourUses = new Dictionary<string, int>();
        void Clean(JsonObject o)
        {
            // Day words put in the time slot ("next saturday") move to the day; other digit-less times ("now", "after work") go.
            foreach (var k in new[] { "day", "time" })
                if (Str(o, k) is { } nm && Regex.Match(Norm(nm), @"^(?:at\s+)?(noon|midday|midnight)$") is { Success: true } nmM)
                { o.Remove(k); o["time"] = nmM.Groups[1].Value == "midnight" ? "12am" : "12pm"; }
            if (Str(o, "time") is { } t0 && !Regex.IsMatch(t0, @"\d")) { o.Remove("time"); if (o["day"] is null) o["day"] = t0; }
            // The user said "next friday" and the model kept only "friday".
            if (Str(o, "day") is { } d0 && dayWord is not null && dayWord.Length > d0.Length && dayWord.EndsWith(Norm(d0))) o["day"] = dayWord;
            if (Str(o, "day") is { } day && !Regex.Split(Norm(day), @"\s+").All(w => words.Contains(w) || words.Contains(w.TrimEnd('s')))) o.Remove("day");
            if (Str(o, "time") is { } time && Regex.Match(time, @"\d{1,2}") is { Success: true } h)
            {
                var hour = h.Value.TrimStart('0').PadLeft(1, '0');
                // Each time the user said can go to one item only, so a time isn't copied onto the next item.
                var spoken = wordList.Count(w => Regex.IsMatch(w, $@"^0?{hour}(\D|$)")) + (hour == "12" ? Regex.Matches(said, @"\b(noon|midday|midnight)\b").Count : 0);
                var used = hourUses.GetValueOrDefault(hour);
                if (used >= spoken) o.Remove("time"); else hourUses[hour] = used + 1;
            }
            if (o["important"] is { } imp && imp.GetValueKind() == JsonValueKind.True && !(said.Contains("important") || phrase.Contains('!'))) o["important"] = false;
        }
        Clean(args);
        if (args["items"] is JsonArray items)
        {
            foreach (var i in items.OfType<JsonObject>()) Clean(i);
            foreach (var i in items.OfType<JsonObject>().ToList())
                if (i["title"]?.GetValue<string>() is not { } title || !Regex.Split(Norm(title), @"[^\p{L}\p{N}]+").Any(w => w.Length > 2 && words.Any(x => x.StartsWith(w.TrimEnd('s')))))
                    items.Remove(i);
            if (items.Count == 0) { call!["name"] = "none"; call["arguments"] = new JsonObject(); }
            if (items.Count > 1 && !Regex.IsMatch(said, @"^\s*(?:today|tonight|tomorrow|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\s*:"))
            {
                var itemParts = Regex.Split(said, @",|;|\band\b").Where(x => x.Trim().Length > 0).ToList();
                bool Owns(JsonObject i, string v) => TitleWords(Str(i, "title")).ToList() is var tw
                    && (tw.Count == 0 || itemParts.Any(pt => Regex.IsMatch(pt, $@"\b{Regex.Escape(v)}") && tw.Any(w => Regex.IsMatch(pt, $@"\b{Regex.Escape(w)}"))));
                var loose = new List<(string Key, string Value, string Said)>();
                foreach (var i in items.OfType<JsonObject>())
                {
                    if (Str(i, "day") is { } idy && !Owns(i, Norm(idy))) { i.Remove("day"); loose.Add(("day", idy, Norm(idy))); }
                    if (Str(i, "time") is { } itm && Regex.Match(itm, @"\d{1,2}") is { Success: true } th && !Owns(i, th.Value.TrimStart('0'))) { i.Remove("time"); loose.Add(("time", itm, th.Value.TrimStart('0'))); }
                }
                // A day or time stuck on the wrong item moves to the item whose part said it ("call mom at 6", not "buy milk").
                foreach (var (key, value, word) in loose)
                    if (items.OfType<JsonObject>().FirstOrDefault(i => i[key] is null && Owns(i, word)) is { } owner) owner[key] = value;
            }
            // Important comes from the words, not the model: the whole phrase for one item, the item's own part of a list for several.
            var marked = said.Contains("important") || phrase.Contains('!');
            var parts = Regex.Split(said, @",|;|\band\b").Where(x => x.Trim().Length > 0).ToList();
            foreach (var i in items.OfType<JsonObject>())
                i["important"] = items.Count == 1 ? marked
                    : parts.Any(part => (part.Contains("important") || part.Contains('!')) && TitleWords(Str(i, "title")).Any(w => Regex.IsMatch(part, $@"\b{Regex.Escape(w)}")));
        }
        return node!.ToJsonString();
    }

    internal static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    internal static IEnumerable<string> TitleWords(string? title) =>
        title is null ? Enumerable.Empty<string>() : Regex.Split(Norm(title), @"[^\p{L}\p{N}]+").Where(w => w.Length > 2 && w is not ("the" or "and" or "for"));

    static readonly Regex Filler = new(@"^(?:(?:remind me|please|add a reminder|add|new reminder|reminder|remember|dont forget|i need|i have|i must|i should)\b[\s:,]*(?:to\b|about\b|that\b)?[\s:,]*)+");
    /// <summary>The phrase's own words for the thing to remember: lead-in filler dropped, cut at the first day/time word, "!" or ", important".</summary>
    static string? Lead(string said)
    {
        var s = Filler.Replace(said, "");
        s = Regex.Replace(s, @"^(?:in the (?:morning|afternoon|evening)|tonight|today|tomorrow)\s+(?:to\s+)?", "");
        var cut = Regex.Match(s, @"\s+(?:(?:on|at|by|before|this|next|the)\s+)*(?:today|tonight|tomorrow|monday|tuesday|wednesday|thursday|friday|saturday|sunday|\d{1,2}(?:st|nd|rd|th|:\d{2}|\s*(?:am|pm))|noon|midday|midnight|in the (?:morning|afternoon|evening))\b|\s*[,!]");
        if (cut.Success) s = s[..cut.Index];
        s = Regex.Replace(s.Trim(), @"\s+(?:on|at|by|before|for|to)$", "");
        return s.Length > 1 ? s : null;
    }

    /// <summary>The create call's items, or null for any other call or unparseable output.</summary>
    public static JsonArray? Items(string output)
    {
        try
        {
            var n = JsonNode.Parse(output);
            var call = n is JsonArray { Count: > 0 } a ? a[0] : n;
            if (call?["name"]?.ToString() != "create_reminders" || call["arguments"]?["items"] is not JsonArray items) return null;
            return (JsonArray)items.DeepClone();
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }
    public static int? ItemCount(string output) => Items(output)?.Count;

    static string Norm(string s) => s.ToLowerInvariant().Replace("'", "").Replace("’", "");
}
