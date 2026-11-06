using System.Text.Json;
using System.Text.RegularExpressions;

namespace Remindr.Tests;

// The spike's 200 eval phrases (tuned set + holdouts 1-5) and its checker, ported from tools/Remindr.AiSpike/Program.cs.
// Expected fields; null means "must be absent or empty". Title/Match are words that must all appear.
internal record Case(string Phrase, string Tool, params Item[] Items);
internal record Item(string? Title = null, string? Day = null, string? Time = null, bool Important = false, string? Match = null,
    string? Snooze = null, bool? Overdue = null, string? Text = null);

internal static class AssistantEval
{
    public static readonly Case[] Cases =
    {
        // Single adds
        new("remind me to call grandma sunday at 5pm, it's important", "create_reminders", new Item("call grandma", "sunday", "17:00", true)),
        new("math test friday 9am !", "create_reminders", new Item("math test", "friday", "09:00", true)),
        new("dentist tomorrow at 10:30", "create_reminders", new Item("dentist", "tomorrow", "10:30")),
        new("buy milk", "create_reminders", new Item("milk")),
        new("pay the electricity bill on the 15th", "create_reminders", new Item("electricity bill", "15th")),
        new("remind me to water the plants tonight at 8", "create_reminders", new Item("water plants", "tonight", "20:00")),
        new("soccer practice next tuesday 4pm", "create_reminders", new Item("soccer practice", "next tuesday", "16:00")),
        new("add a reminder: renew passport, important", "create_reminders", new Item("renew passport", Important: true)),
        new("pick up Sam from school at 3:15 today", "create_reminders", new Item("pick up sam", "today", "15:15")),
        new("don't forget mom's birthday on saturday", "create_reminders", new Item("mom birthday", "saturday")),
        new("Remind me in the morning to take my vitamins", "create_reminders", new Item("vitamins", "morning")),
        new("vet appointment for the dog wednesday 11am", "create_reminders", new Item("vet", "wednesday", "11:00")),
        new("call the plumber tomorrow", "create_reminders", new Item("plumber", "tomorrow")),
        new("piano lesson thursday at 6:30pm", "create_reminders", new Item("piano lesson", "thursday", "18:30")),
        new("book flights before friday, this is important", "create_reminders", new Item("book flights", "friday", Important: true)),
        // Multi-item
        new("remind me to buy milk, call mom at 6 and pay rent friday, rent is important", "create_reminders",
            new Item("milk"), new Item("call mom", Time: "18:00"), new Item("pay rent", "friday", Important: true)),
        new("tomorrow: gym at 7am and groceries after work", "create_reminders",
            new Item("gym", "tomorrow", "07:00"), new Item("groceries", "tomorrow")),
        new("add eggs, bread and butter to my reminders", "create_reminders", new Item("eggs"), new Item("bread"), new Item("butter")),
        new("homework due monday and science project due wednesday", "create_reminders",
            new Item("homework", "monday"), new Item("science project", "wednesday")),
        new("haircut saturday 10am, car wash sunday", "create_reminders", new Item("haircut", "saturday", "10:00"), new Item("car wash", "sunday")),
        // Find
        new("what's on tomorrow", "find_reminders", new Item(Day: "tomorrow")),
        new("what do I have today?", "find_reminders", new Item(Day: "today")),
        new("what's overdue", "find_reminders", new Item(Overdue: true)),
        new("show my important reminders", "find_reminders", new Item(Important: true)),
        new("anything on friday?", "find_reminders", new Item(Day: "friday")),
        new("when is the dentist", "find_reminders", new Item(Text: "dentist")),
        new("what's coming up this week", "find_reminders", new Item(Day: "this week")),
        new("did I miss anything", "find_reminders", new Item(Overdue: true)),
        // Complete
        new("mark milk done", "complete_reminder", new Item(Match: "milk")),
        new("I called grandma", "complete_reminder", new Item(Match: "grandma")),
        new("done with the math homework", "complete_reminder", new Item(Match: "math homework")),
        new("tick off the dentist", "complete_reminder", new Item(Match: "dentist")),
        new("finished paying rent", "complete_reminder", new Item(Match: "rent")),
        // Snooze
        new("snooze dentist 1 hour", "snooze_reminder", new Item(Match: "dentist", Snooze: "1 hour")),
        new("remind me about the gym again in 5 minutes", "snooze_reminder", new Item(Match: "gym", Snooze: "5 minutes")),
        new("push the plumber call to tomorrow", "snooze_reminder", new Item(Match: "plumber", Snooze: "tomorrow")),
        new("snooze piano for an hour", "snooze_reminder", new Item(Match: "piano", Snooze: "1 hour")),
        // Delete
        new("delete the gym one", "delete_reminder", new Item(Match: "gym")),
        new("remove the car wash reminder", "delete_reminder", new Item(Match: "car wash")),
        new("cancel soccer practice", "delete_reminder", new Item(Match: "soccer practice")),
        // Update
        new("make the dentist one important", "update_reminder", new Item(Match: "dentist", Important: true)),
        new("move piano lesson to tuesday 3pm", "update_reminder", new Item(Match: "piano", Day: "tuesday", Time: "15:00")),
        new("change the vet appointment to 2pm", "update_reminder", new Item(Match: "vet", Time: "14:00")),
        new("rename milk to oat milk", "update_reminder", new Item(Match: "milk", Title: "oat milk")),
        new("move haircut to next saturday", "update_reminder", new Item(Match: "haircut", Day: "next saturday")),
        // Nothing to do
        new("hello", "none"),
        new("what's the weather like", "none"),
        new("thanks!", "none"),
        new("tell me a joke", "none"),
        new("how are you", "none"),
    };

    /// <summary>Written before the guard rules for them, and never tuned against: this score is the honest one.</summary>
    public static readonly Case[] Holdout =
    {
        new("remind me to email the teacher monday at 8am", "create_reminders", new Item("email teacher", "monday", "08:00")),
        new("grocery shopping saturday", "create_reminders", new Item("grocery shopping", "saturday")),
        new("take out the trash tonight", "create_reminders", new Item("trash", "tonight")),
        new("call the bank tomorrow at 9:15, important", "create_reminders", new Item("call bank", "tomorrow", "09:15", true)),
        new("doctor appointment next friday at 2pm", "create_reminders", new Item("doctor appointment", "next friday", "14:00")),
        new("remember to feed the cat", "create_reminders", new Item("feed cat")),
        new("submit the report by thursday !", "create_reminders", new Item("submit report", "thursday", Important: true)),
        new("band practice wednesday 7pm", "create_reminders", new Item("band practice", "wednesday", "19:00")),
        new("pick up dry cleaning today", "create_reminders", new Item("dry cleaning", "today")),
        new("buy batteries, charge the laptop and print tickets", "create_reminders", new Item("batteries"), new Item("charge laptop"), new Item("print tickets")),
        new("dinner with Alex friday 7:30pm, lunch with Jo saturday", "create_reminders", new Item("dinner alex", "friday", "19:30"), new Item("lunch jo", "saturday")),
        new("what's on monday", "find_reminders", new Item(Day: "monday")),
        new("show me what's overdue", "find_reminders", new Item(Overdue: true)),
        new("do I have anything important", "find_reminders", new Item(Important: true)),
        new("when is band practice", "find_reminders", new Item(Text: "band practice")),
        new("what's planned for today", "find_reminders", new Item(Day: "today")),
        new("I fed the cat", "complete_reminder", new Item(Match: "cat")),
        new("mark the report as done", "complete_reminder", new Item(Match: "report")),
        new("finished the grocery shopping", "complete_reminder", new Item(Match: "grocery shopping")),
        new("snooze the trash reminder for 10 minutes", "snooze_reminder", new Item(Match: "trash", Snooze: "10 minutes")),
        new("remind me about the bank again in 2 hours", "snooze_reminder", new Item(Match: "bank", Snooze: "2 hours")),
        new("delete the dinner reminder", "delete_reminder", new Item(Match: "dinner")),
        new("cancel band practice", "delete_reminder", new Item(Match: "band practice")),
        new("get rid of the batteries one", "delete_reminder", new Item(Match: "batteries")),
        new("move the doctor appointment to monday", "update_reminder", new Item(Match: "doctor", Day: "monday")),
        new("change band practice to 8pm", "update_reminder", new Item(Match: "band practice", Time: "20:00")),
        new("make the report important", "update_reminder", new Item(Match: "report", Important: true)),
        new("good morning", "none"),
        new("what time is it", "none"),
        new("can you play music", "none"),
    };

    public static readonly Case[] Holdout2 =
    {
        new("remind me to stretch at 7am", "create_reminders", new Item("stretch", Time: "07:00")),
        new("laundry sunday", "create_reminders", new Item("laundry", "sunday")),
        new("return library books tomorrow, important", "create_reminders", new Item("library books", "tomorrow", Important: true)),
        new("parent teacher meeting thursday at 4:45pm", "create_reminders", new Item("parent teacher meeting", "thursday", "16:45")),
        new("don't forget to charge my phone tonight", "create_reminders", new Item("charge phone", "tonight")),
        new("send the invoice monday !", "create_reminders", new Item("send invoice", "monday", Important: true)),
        new("change the oil next week", "create_reminders", new Item("oil", "next week")),
        new("water the garden, walk the dog and call dad", "create_reminders", new Item("water garden"), new Item("walk dog"), new Item("call dad")),
        new("study for the exam tuesday at 6pm and review notes wednesday", "create_reminders", new Item("study exam", "tuesday", "18:00"), new Item("review notes", "wednesday")),
        new("buy a birthday card for Lisa", "create_reminders", new Item("birthday card lisa")),
        new("what's happening tomorrow", "find_reminders", new Item(Day: "tomorrow")),
        new("anything due this week?", "find_reminders", new Item(Day: "this week")),
        new("list my important stuff", "find_reminders", new Item(Important: true)),
        new("when's the parent teacher meeting", "find_reminders", new Item(Text: "parent teacher meeting")),
        new("have I missed anything?", "find_reminders", new Item(Overdue: true)),
        new("I did the laundry", "complete_reminder", new Item(Match: "laundry")),
        new("done with the invoice", "complete_reminder", new Item(Match: "invoice")),
        new("mark stretching as complete", "complete_reminder", new Item(Match: "stretch")),
        new("I returned the library books", "complete_reminder", new Item(Match: "library books")),
        new("snooze the oil change until tomorrow", "snooze_reminder", new Item(Match: "oil", Snooze: "tomorrow")),
        new("remind me about laundry again in 30 minutes", "snooze_reminder", new Item(Match: "laundry", Snooze: "30 minutes")),
        new("delete the birthday card reminder", "delete_reminder", new Item(Match: "birthday card")),
        new("remove laundry", "delete_reminder", new Item(Match: "laundry")),
        new("cancel the parent teacher meeting", "delete_reminder", new Item(Match: "parent teacher meeting")),
        new("move laundry to saturday", "update_reminder", new Item(Match: "laundry", Day: "saturday")),
        new("change the invoice to 10am", "update_reminder", new Item(Match: "invoice", Time: "10:00")),
        new("rename stretch to morning yoga", "update_reminder", new Item(Match: "stretch", Title: "morning yoga")),
        new("good night", "none"),
        new("who won the game last night", "none"),
        new("open spotify", "none"),
    };

    public static readonly Case[] Holdout3 =
    {
        new("remind me to check the mail at noon", "create_reminders", new Item("check mail", Time: "12:00")),
        new("swim lesson friday 5pm", "create_reminders", new Item("swim lesson", "friday", "17:00")),
        new("renew car insurance next month, important", "create_reminders", new Item("renew car insurance", "next month", Important: true)),
        new("buy flowers for mom tomorrow morning", "create_reminders", new Item("flowers mom", "tomorrow")),
        new("call the dentist to reschedule", "create_reminders", new Item("call dentist")),
        new("pack lunch tonight !", "create_reminders", new Item("pack lunch", "tonight", Important: true)),
        new("meeting with Priya monday at 11:30am", "create_reminders", new Item("meeting priya", "monday", "11:30")),
        new("pick up milk and bread", "create_reminders", new Item("milk bread")),
        new("clean the kitchen saturday and mow the lawn sunday", "create_reminders", new Item("clean kitchen", "saturday"), new Item("mow lawn", "sunday")),
        new("pay phone bill, cancel gym membership and book a haircut", "create_reminders", new Item("pay phone bill"), new Item("cancel gym membership"), new Item("book haircut")),
        new("what do I have on saturday", "find_reminders", new Item(Day: "saturday")),
        new("is there anything overdue", "find_reminders", new Item(Overdue: true)),
        new("show everything important", "find_reminders", new Item(Important: true)),
        new("when do I see Priya", "find_reminders", new Item(Text: "priya")),
        new("what's left for today?", "find_reminders", new Item(Day: "today")),
        new("I checked the mail", "complete_reminder", new Item(Match: "mail")),
        new("mark swim lesson done", "complete_reminder", new Item(Match: "swim lesson")),
        new("finished cleaning the kitchen", "complete_reminder", new Item(Match: "kitchen")),
        new("snooze the flowers reminder for an hour", "snooze_reminder", new Item(Match: "flowers", Snooze: "1 hour")),
        new("remind me about the lunch again in 15 minutes", "snooze_reminder", new Item(Match: "lunch", Snooze: "15 minutes")),
        new("delete the swim lesson", "delete_reminder", new Item(Match: "swim lesson")),
        new("cancel the meeting with Priya", "delete_reminder", new Item(Match: "meeting priya")),
        new("remove the phone bill reminder", "delete_reminder", new Item(Match: "phone bill")),
        new("move the swim lesson to thursday", "update_reminder", new Item(Match: "swim lesson", Day: "thursday")),
        new("change the meeting with Priya to 1pm", "update_reminder", new Item(Match: "meeting priya", Time: "13:00")),
        new("make the insurance one important", "update_reminder", new Item(Match: "insurance", Important: true)),
        new("rename lunch to pack school lunch", "update_reminder", new Item(Match: "lunch", Title: "pack school lunch")),
        new("hey there", "none"),
        new("what's 12 times 8", "none"),
        new("play some jazz", "none"),
    };

    public static readonly Case[] Holdout4 =
    {
        new("remind me to defrost the chicken at 4pm", "create_reminders", new Item("defrost chicken", Time: "16:00")),
        new("guitar lesson tuesday", "create_reminders", new Item("guitar lesson", "tuesday")),
        new("file my taxes before friday, important", "create_reminders", new Item("taxes", "friday", Important: true)),
        new("hand in the permission slip tomorrow", "create_reminders", new Item("permission slip", "tomorrow")),
        new("schedule a vaccine appointment", "create_reminders", new Item("vaccine appointment")),
        new("wrap presents tonight !", "create_reminders", new Item("wrap presents", "tonight", Important: true)),
        new("coffee with Marco wednesday at 10:15am", "create_reminders", new Item("coffee marco", "wednesday", "10:15")),
        new("unsubscribe from the newsletter", "create_reminders", new Item("unsubscribe newsletter")),
        new("buy sunscreen, fix the bike light and email the landlord", "create_reminders", new Item("sunscreen"), new Item("bike light"), new Item("email landlord")),
        new("vacuum the stairs friday and dust the shelves saturday", "create_reminders", new Item("vacuum stairs", "friday"), new Item("dust shelves", "saturday")),
        new("what's on wednesday", "find_reminders", new Item(Day: "wednesday")),
        new("do I have anything overdue?", "find_reminders", new Item(Overdue: true)),
        new("show the important ones", "find_reminders", new Item(Important: true)),
        new("when is coffee with Marco", "find_reminders", new Item(Text: "marco")),
        new("what's coming up tomorrow", "find_reminders", new Item(Day: "tomorrow")),
        new("I wrapped the presents", "complete_reminder", new Item(Match: "presents")),
        new("mark the taxes as done", "complete_reminder", new Item(Match: "taxes")),
        new("done with the guitar lesson", "complete_reminder", new Item(Match: "guitar lesson")),
        new("snooze the chicken for 10 minutes", "snooze_reminder", new Item(Match: "chicken", Snooze: "10 minutes")),
        new("remind me about the slip again tomorrow", "snooze_reminder", new Item(Match: "slip", Snooze: "tomorrow")),
        new("delete the newsletter one", "delete_reminder", new Item(Match: "newsletter")),
        new("remove coffee with Marco", "delete_reminder", new Item(Match: "marco")),
        new("cancel the guitar lesson", "delete_reminder", new Item(Match: "guitar lesson")),
        new("move the guitar lesson to monday", "update_reminder", new Item(Match: "guitar lesson", Day: "monday")),
        new("change coffee with Marco to 11am", "update_reminder", new Item(Match: "marco", Time: "11:00")),
        new("make the taxes one important", "update_reminder", new Item(Match: "taxes", Important: true)),
        new("rename presents to wrap gifts", "update_reminder", new Item(Match: "presents", Title: "wrap gifts")),
        new("yo", "none"),
        new("how tall is mount everest", "none"),
        new("set the volume to 50", "none"),
    };

    public static readonly Case[] Holdout5 =
    {
        new("remind me to refill the prescription thursday at 8:30am", "create_reminders", new Item("refill prescription", "thursday", "08:30")),
        new("book club monday evening", "create_reminders", new Item("book club", "monday")),
        new("renew my passport, important", "create_reminders", new Item("renew passport", Important: true)),
        new("bring the umbrella tomorrow", "create_reminders", new Item("umbrella", "tomorrow")),
        new("text Nina about the party", "create_reminders", new Item("text nina party")),
        new("practice the speech tonight !", "create_reminders", new Item("practice speech", "tonight", Important: true)),
        new("lunch with Omar friday at 12:30pm", "create_reminders", new Item("lunch omar", "friday", "12:30")),
        new("cancel the cable subscription", "delete_reminder", new Item(Match: "cable subscription")),
        new("buy cat food, recycle the bottles and call grandpa", "create_reminders", new Item("cat food"), new Item("recycle bottles"), new Item("call grandpa")),
        new("bake cookies saturday and visit aunt May sunday", "create_reminders", new Item("bake cookies", "saturday"), new Item("visit aunt may", "sunday")),
        new("what's on friday?", "find_reminders", new Item(Day: "friday")),
        new("anything overdue", "find_reminders", new Item(Overdue: true)),
        new("show me the important reminders", "find_reminders", new Item(Important: true)),
        new("when is lunch with Omar", "find_reminders", new Item(Text: "omar")),
        new("what do I have tomorrow?", "find_reminders", new Item(Day: "tomorrow")),
        new("I baked the cookies", "complete_reminder", new Item(Match: "cookies")),
        new("mark the speech as done", "complete_reminder", new Item(Match: "speech")),
        new("finished the prescription refill", "complete_reminder", new Item(Match: "prescription")),
        new("snooze the umbrella reminder for 5 minutes", "snooze_reminder", new Item(Match: "umbrella", Snooze: "5 minutes")),
        new("remind me about book club again in an hour", "snooze_reminder", new Item(Match: "book club", Snooze: "1 hour")),
        new("delete lunch with Omar", "delete_reminder", new Item(Match: "omar")),
        new("remove the cookies one", "delete_reminder", new Item(Match: "cookies")),
        new("get rid of the speech reminder", "delete_reminder", new Item(Match: "speech")),
        new("move book club to tuesday", "update_reminder", new Item(Match: "book club", Day: "tuesday")),
        new("change the prescription to 9am", "update_reminder", new Item(Match: "prescription", Time: "09:00")),
        new("make the passport one important", "update_reminder", new Item(Match: "passport", Important: true)),
        new("rename umbrella to raincoat", "update_reminder", new Item(Match: "umbrella", Title: "raincoat")),
        new("hiya", "none"),
        new("what's the capital of france", "none"),
        new("dim the screen", "none"),
    };

    /// <summary>Null when the output matches; otherwise what was wrong.</summary>
    public static string? Check(Case c, string output)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(output).RootElement; if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0]; } catch (JsonException) { return "not valid JSON"; }
        var tool = root.TryGetProperty("name", out var t) ? t.GetString() : null;
        if (tool != c.Tool) return $"tool {tool}, want {c.Tool}";
        if (c.Items.Length == 0) return null;
        var args = root.GetProperty("arguments");
        if (c.Tool == "create_reminders")
        {
            if (!args.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return "no items list";
            var got = arr.EnumerateArray().ToList();
            if (got.Count != c.Items.Length) return $"{got.Count} items, want {c.Items.Length}";
            for (var i = 0; i < got.Count; i++) if (Fields(c.Items[i], got[i]) is { } p) return $"item {i + 1}: {p}";
            return null;
        }
        return Fields(c.Items[0], args);
    }

    private static string? Fields(Item want, JsonElement got)
    {
        string? S(string k) => got.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() is { Length: > 0 } s ? s.ToLowerInvariant() : null;
        bool B(string k) => got.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

        if (want.Title is { } title && !Words(title, S("title"))) return $"title \"{S("title")}\", want \"{title}\"";
        if (want.Match is { } match && !Words(match, S("match") ?? S("title"))) return $"match \"{S("match")}\", want \"{match}\"";
        if (!Same(want.Day, S("day"))) return $"day \"{S("day")}\", want \"{want.Day}\"";
        if (want.Time != Time(S("time"))) return $"time \"{S("time")}\", want \"{want.Time}\"";
        if (want.Important != B("important")) return $"important {B("important")}, want {want.Important}";
        if (want.Snooze is { } sn && !Snooze(sn, S("snooze"))) return $"snooze \"{S("snooze")}\", want \"{sn}\"";
        if (want.Overdue is true && !B("overdue")) return "overdue not set";
        if (want.Text is { } text && !Words(text, S("text") ?? S("match") ?? S("title"))) return $"text \"{S("text")}\", want \"{text}\"";
        return null;
    }

    // Every word of want (minus "the"/"my") appears in got, ignoring plurals and apostrophes.
    private static bool Words(string want, string? got) =>
        got is not null && want.Split(' ').Where(w => w is not ("the" or "my")).All(w => Norm(got).Contains(Norm(w).TrimEnd('s')));
    private static string Norm(string s) => s.ToLowerInvariant().Replace("'", "").Replace("’", "");
    private static bool Same(string? want, string? got) =>
        want is null ? got is null : got is not null && Regex.Replace(Norm(got).Replace("on ", "").Trim(), @"\s+(?:morning|afternoon|evening|night)$", "") == want;
    private static bool Snooze(string want, string? got) =>
        got is not null && SnoozeNorm(got).Contains(SnoozeNorm(want));
    private static string SnoozeNorm(string s) => Regex.Replace(s, @"\ban\b|\ba\b", "1").Replace("hours", "hour").Replace("mins", "minutes").Replace("min ", "minutes");

    /// <summary>"9am", "9:00 am", "17:00", "at 6", "8" → "HH:mm".</summary>
    private static string? Time(string? s)
    {
        if (s is null) return null;
        var m = Regex.Match(s, @"(\d{1,2})(?::(\d{2}))?\s*(am|pm|a\.m\.|p\.m\.)?");
        if (!m.Success) return s;
        var h = int.Parse(m.Groups[1].Value);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        var pm = m.Groups[3].Value.StartsWith('p');
        var am = m.Groups[3].Value.StartsWith('a');
        if (pm && h < 12) h += 12;
        if (am && h == 12) h = 0;
        if (!pm && !am && h is >= 1 and <= 8) h += 12;   // a bare "at 6", "at 8" or "3:15" means the afternoon or evening, as the app's own time parser would guess
        return $"{h:00}:{min:00}";
    }
}
