using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

// Usage:  dotnet run --project tools\Remindr.AiSpike -c Release -- --check
//         dotnet run --project tools\Remindr.AiSpike -c Release -- <model.gguf> [threads]
// The model only extracts fields (title, day words, time words, important); dates are never its job.

if (args.Length == 0) { Console.WriteLine("Pass --check or a .gguf path."); return 1; }

// 1. Do the native llama.cpp files load at all on this PC (Application Control blocked a built DLL here before)?
// LLamaSharp must target net8.0 here (0.30 dropped it, and its netstandard2.0 build fails to find the files).
var gpus = new List<(int Index, string Name, bool SharesRam)>();
string? usedDevice = null;
(int Done, int Of) offloaded = (0, 0);
try
{
    // Show llama.cpp only the NVIDIA card: the AMD 780M is an iGPU whose "VRAM" is system RAM.
    // SPIKE_CPU=1 forces the CPU build; otherwise Vulkan (NVIDIA, AMD, Intel) with CPU as the fallback.
    // Only llama.cpp's device and offload lines are shown, as proof of where the layers went.
    NativeLibraryConfig.All.WithVulkan(Environment.GetEnvironmentVariable("SPIKE_CPU") != "1").WithAutoFallback(true)
        .WithLogCallback((_, msg) =>
        {
            if (!Regex.IsMatch(msg, @"ggml_vulkan: \d|offloaded|using device|error|fail") && Environment.GetEnvironmentVariable("SPIKE_LOG") != "1") return;
            Console.Write("  [llama] " + msg);
            if (Regex.Match(msg, @"ggml_vulkan: (\d+) = (.+?)(?: \([^)]*\))? \|.*uma: (\d)") is { Success: true } d) gpus.Add((int.Parse(d.Groups[1].Value), d.Groups[2].Value, d.Groups[3].Value == "1"));
            if (msg.Contains("using device")) usedDevice = msg;
            if (Regex.Match(msg, @"offloaded (\d+)/(\d+) layers") is { Success: true } o) offloaded = (int.Parse(o.Groups[1].Value), int.Parse(o.Groups[2].Value));
        });
    Console.WriteLine($"Native library loaded. GPU offload supported: {NativeApi.llama_supports_gpu_offload()}");
}
catch (Exception ex)
{
    Console.WriteLine("Native library FAILED to load: " + ex.GetBaseException().Message);
    return 2;
}
if (args[0] == "--check") return 0;

// SPIKE_RAW=<file>: a model run saves every raw answer there; "--replay" reruns the guard on them with no model at all.
var rawPath = Environment.GetEnvironmentVariable("SPIKE_RAW");
var raws = rawPath is not null && File.Exists(rawPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(rawPath))! : new();
Func<string, Task<string>> infer = p => raws.TryGetValue(p, out var r) ? Task.FromResult(r) : throw new KeyNotFoundException("No saved answer for: " + p);
if (args[0] == "--replay") { await Run("Tuned set", Eval.Cases); await Run("Holdout", Eval.Holdout); await Run("Holdout 2", Eval.Holdout2); await Run("Holdout 3", Eval.Holdout3); await Run("Holdout 4", Eval.Holdout4); await Run("Holdout 5", Eval.Holdout5); return 0; }

var path = args[0];
var threads = args.Length > 1 ? int.Parse(args[1]) : Math.Max(1, Environment.ProcessorCount / 2);
var cpuOnly = Environment.GetEnvironmentVariable("SPIKE_CPU") == "1";
// A dedicated card (uma: 0) has its own VRAM; an integrated one (uma: 1) borrows system RAM.
var gpu = gpus.Where(g => !g.SharesRam).Select(g => ((int Index, string Name)?)(g.Index, g.Name)).FirstOrDefault();
if (!cpuOnly && gpu is null) { Console.WriteLine("No dedicated GPU found (only integrated ones that use system RAM). Set SPIKE_CPU=1 to run on the CPU."); return 3; }
if (!cpuOnly) Console.WriteLine($"Using GPU {gpu!.Value.Index}: {gpu.Value.Name}");
var mp = new ModelParams(path) { ContextSize = 2048, GpuLayerCount = Environment.GetEnvironmentVariable("SPIKE_CPU") == "1" ? 0 : 99, Threads = threads,
    // llama.cpp lists only dedicated cards when one exists (the AMD iGPU drops out), so device 0 is the RTX 4060 whatever Vulkan numbers it.
    // Never set GGML_VK_VISIBLE_DEVICES by number: Vulkan reorders devices, and "0" once meant the iGPU. The check after loading enforces this.
    SplitMode = LLama.Native.GPUSplitMode.None, MainGpu = 0,
    // No memory map: the weights are read straight into VRAM instead of also staying mapped in system RAM.
    UseMemorymap = false };
var load = Stopwatch.StartNew();
using var weights = LLamaWeights.LoadFromFile(mp);
load.Stop();
// Refuse to measure anything if the model didn't land fully on the chosen card.
if (!cpuOnly && (usedDevice is null || !usedDevice.Contains(gpu!.Value.Name) || offloaded.Done != offloaded.Of || offloaded.Of == 0))
{ Console.WriteLine($"Model is NOT fully on {gpu!.Value.Name} ({offloaded.Done}/{offloaded.Of} layers, device line: {usedDevice?.Trim()}). Stopping."); return 4; }
var executor = new StatelessExecutor(weights, mp);
Console.WriteLine($"Model: {Path.GetFileName(path)}, loaded in {load.Elapsed.TotalSeconds:F1}s, {threads} threads");

// LFM2.5's own tool format (model card): "List of tools: [JSON]" in the system message; JSON calls when asked for them.
const string Day = """{"type": "string", "description": "Day words exactly as said, e.g. friday, tomorrow, next tuesday. Never a date."}""";
const string Time = """{"type": "string", "description": "Time words exactly as said, e.g. 9am, 5:30pm."}""";
const string Match = """{"type": "string", "description": "Words naming the existing reminder, e.g. dentist."}""";
const string SystemPromptRaw = """
You are the assistant inside Remindr, a reminders app. Call exactly one tool for the user's message. Only include arguments the user said.
List of tools: [
{"name": "create_reminders", "description": "Add new reminders. One item per separate thing to remember.", "parameters": {"type": "object", "properties": {"items": {"type": "array", "items": {"type": "object", "properties": {"title": {"type": "string", "description": "What to remember, in the user's own words"}, "day": @DAY@, "time": @TIME@, "important": {"type": "boolean", "description": "True only if the user says important or uses !"}}}}}}},
{"name": "find_reminders", "description": "Answer questions about what is planned, due, overdue or important.", "parameters": {"type": "object", "properties": {"day": @DAY@, "important": {"type": "boolean"}, "overdue": {"type": "boolean"}, "text": {"type": "string", "description": "Words to search for"}}}},
{"name": "complete_reminder", "description": "Mark an existing reminder as done.", "parameters": {"type": "object", "properties": {"match": @MATCH@}}},
{"name": "delete_reminder", "description": "Delete or cancel an existing reminder.", "parameters": {"type": "object", "properties": {"match": @MATCH@}}},
{"name": "snooze_reminder", "description": "Remind again later about an existing reminder.", "parameters": {"type": "object", "properties": {"match": @MATCH@, "snooze": {"type": "string", "description": "e.g. 5 minutes, 1 hour, tomorrow"}}}},
{"name": "update_reminder", "description": "Change an existing reminder: rename, move to another day or time, or make it important.", "parameters": {"type": "object", "properties": {"match": @MATCH@, "title": {"type": "string", "description": "New title"}, "day": @DAY@, "time": @TIME@, "important": {"type": "boolean"}}}},
{"name": "none", "description": "The message is chat or asks for something the app cannot do.", "parameters": {"type": "object", "properties": {}}}
]
Output function calls as JSON.
""";
var SystemPrompt = SystemPromptRaw.Replace("@DAY@", Day).Replace("@TIME@", Time).Replace("@MATCH@", Match);

// Worked examples shown to the model before each message (SPIKE_SHOTS=0 turns them off to compare).
(string User, string Call)[] Shots =
{
    ("remind me to wax the bike saturday at 9, it's important", """[{"name": "create_reminders", "arguments": {"items": [{"title": "wax the bike", "day": "saturday", "time": "9", "important": true}]}}]"""),
    ("order pizza, text Ben and buy stamps", """[{"name": "create_reminders", "arguments": {"items": [{"title": "order pizza"}, {"title": "text Ben"}, {"title": "buy stamps"}]}}]"""),
    ("cancel my magazine subscription friday", """[{"name": "create_reminders", "arguments": {"items": [{"title": "cancel my magazine subscription", "day": "friday"}]}}]"""),
    ("what's on thursday?", """[{"name": "find_reminders", "arguments": {"day": "thursday"}}]"""),
    ("I watered the cactus", """[{"name": "complete_reminder", "arguments": {"match": "cactus"}}]"""),
    ("snooze choir rehearsal for 20 minutes", """[{"name": "snooze_reminder", "arguments": {"match": "choir rehearsal", "snooze": "20 minutes"}}]"""),
    ("delete choir rehearsal", """[{"name": "delete_reminder", "arguments": {"match": "choir rehearsal"}}]"""),
    ("move choir rehearsal to 6pm", """[{"name": "update_reminder", "arguments": {"match": "choir rehearsal", "time": "6pm"}}]"""),
    ("rename choir rehearsal to concert prep", """[{"name": "update_reminder", "arguments": {"match": "choir rehearsal", "title": "concert prep"}}]"""),
    ("turn on the lights", """[{"name": "none", "arguments": {}}]"""),
};

// The model's own call shape, [{"name": tool, "arguments": {...}}], and nothing after it.
// Each tool's required field comes first and can't be empty: create needs items with a title, the others a match.
const string Gbnf = """
root   ::= "[" ws "{" ws "\"name\"" ws ":" ws call ws "}" ws "]"
call   ::= create | find | target | none
args   ::= ws "," ws "\"arguments\"" ws ":" ws
create ::= "\"create_reminders\"" args "{" ws "\"items\"" ws ":" ws "[" ws item ( ws "," ws item )* ws "]" ws "}"
item   ::= "{" ws "\"title\"" ws ":" ws text ( ws "," ws ( day | time | imp ) )* ws "}"
day    ::= "\"day\"" ws ":" ws text
time   ::= "\"time\"" ws ":" ws text
imp    ::= "\"important\"" ws ":" ws bool
bool   ::= "true" | "false"
find   ::= "\"find_reminders\"" args "{" ws ( fkey ( ws "," ws fkey )* )? ws "}"
fkey   ::= day | imp | "\"overdue\"" ws ":" ws bool | "\"text\"" ws ":" ws text
target ::= tdone | tsnooze | tupdate
tdone  ::= ( "\"complete_reminder\"" | "\"delete_reminder\"" ) args "{" ws match ws "}"
tsnooze ::= "\"snooze_reminder\"" args "{" ws match ( ws "," ws "\"snooze\"" ws ":" ws text )? ws "}"
tupdate ::= "\"update_reminder\"" args "{" ws match ( ws "," ws ( day | time | imp | "\"title\"" ws ":" ws text ) )* ws "}"
match  ::= "\"match\"" ws ":" ws text
none   ::= "\"none\"" args "{" ws "}"
text   ::= "\"" char+ "\""
char   ::= [^"\\\x7F\x00-\x1F] | "\\" (["\\/bfnrt] | "u" [0-9a-fA-F]{4})
value  ::= object | array | string | number | ("true" | "false" | "null")
object ::= "{" ws ( string ws ":" ws value ( ws "," ws string ws ":" ws value )* )? ws "}"
array  ::= "[" ws ( value ( ws "," ws value )* )? ws "]"
string ::= "\"" ( [^"\\\x7F\x00-\x1F] | "\\" (["\\/bfnrt] | "u" [0-9a-fA-F]{4}) )* "\""
number ::= "-"? [0-9]+ ("." [0-9]+)?
ws     ::= [ \t\n]{0,3}
""";

// SPIKE_DEBUG=1: show the prompt and the model's raw answer without the grammar, for a few phrases.
var debug = Environment.GetEnvironmentVariable("SPIKE_DEBUG") == "1";

async Task<string> InferModel(string phrase)
{
    var template = new LLamaTemplate(weights) { AddAssistant = true };
    template.Add("system", SystemPrompt);
    if (Environment.GetEnvironmentVariable("SPIKE_SHOTS") != "0")
        foreach (var (u, c) in Shots) { template.Add("user", u); template.Add("assistant", c); }
    template.Add("user", phrase);
    var prompt = Encoding.UTF8.GetString(template.Apply());
    if (debug) Console.WriteLine("---- PROMPT ----\n" + prompt + "\n---- END ----");
    var inference = new InferenceParams
    {
        MaxTokens = 256,
        SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0f, Grammar = debug ? null : new Grammar(Gbnf, "root") },
    };
    var sb = new StringBuilder();
    await foreach (var t in executor.InferAsync(prompt, inference)) sb.Append(t);
    return sb.ToString().Trim();
}

infer = async p => raws[p] = await InferModel(p);

// The app's path: model, guard, and for a comma list the model missed items from, one model call per part.
async Task<(string Raw, string Final)> Resolve(string phrase)
{
    var raw = await infer(phrase);
    var guarded = Eval.Guard(phrase, raw);
    var parts = Eval.Segments(phrase);
    if (parts.Count < 2 || Eval.ItemCount(guarded) is not { } have || have >= parts.Count) return (raw, guarded);
    var items = new System.Text.Json.Nodes.JsonArray();
    foreach (var part in parts)
        if (Eval.Items(Eval.Guard(part, await infer(part))) is { } got) foreach (var i in got) items.Add(i!.DeepClone());
    foreach (var what in Eval.MarkedImportant(phrase))
        foreach (var i in items.OfType<System.Text.Json.Nodes.JsonObject>())
            if (i["title"]?.ToString() is { } t && Regex.IsMatch(t.ToLowerInvariant(), $@"\b{Regex.Escape(what)}")) i["important"] = true;
    return (raw, """[{"name":"create_reminders","arguments":{"items":""" + items.ToJsonString() + "}}]");
}

async Task Run(string label, IEnumerable<Case> cases)
{
    int pass = 0, rawPass = 0, total = 0;
    var latencies = new List<double>();
    Console.WriteLine($"\n== {label}");
    foreach (var c in cases)
    {
        var sw = Stopwatch.StartNew();
        var (raw, final) = await Resolve(c.Phrase);
        sw.Stop();
        latencies.Add(sw.Elapsed.TotalSeconds);
        total++;
        if (Eval.Check(c, raw) is null) rawPass++;
        var problem = Eval.Check(c, final);
        if (problem is null) pass++;
        Console.WriteLine($"{(problem is null ? "PASS" : "FAIL")} {sw.Elapsed.TotalSeconds,5:F1}s  {c.Phrase}");
        if (problem is not null) Console.WriteLine($"       {problem}\n       got: {final}");
    }
    latencies.Sort();
    Console.WriteLine($"{label} raw model: {rawPass}/{total} = {100.0 * rawPass / total:F0}%");
    Console.WriteLine($"{label} score: {pass}/{total} = {100.0 * pass / total:F0}%   median {latencies[latencies.Count / 2]:F1}s, slowest {latencies[^1]:F1}s");
}

if (debug) await Run("Debug", Eval.Cases.Where((_, i) => i is 0 or 20 or 28 or 45));
else { await Run("Tuned set", Eval.Cases); await Run("Holdout", Eval.Holdout); await Run("Holdout 2", Eval.Holdout2); await Run("Holdout 3", Eval.Holdout3); await Run("Holdout 4", Eval.Holdout4); await Run("Holdout 5", Eval.Holdout5); }
Console.WriteLine($"Peak memory: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");
if (rawPath is not null) File.WriteAllText(rawPath, JsonSerializer.Serialize(raws));
return 0;

record Case(string Phrase, string Tool, params Item[] Items);
/// <summary>Expected fields; null means "must be absent or empty". Title/Match are words that must all appear.</summary>
record Item(string? Title = null, string? Day = null, string? Time = null, bool Important = false, string? Match = null,
    string? Snooze = null, bool? Overdue = null, string? Text = null);

static class Eval
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

    /// <summary>The create call's items, or null for any other call or unparseable output.</summary>
    public static System.Text.Json.Nodes.JsonArray? Items(string output)
    {
        try
        {
            var n = System.Text.Json.Nodes.JsonNode.Parse(output);
            var call = n is System.Text.Json.Nodes.JsonArray { Count: > 0 } a ? a[0] : n;
            if (call?["name"]?.ToString() != "create_reminders" || call["arguments"]?["items"] is not System.Text.Json.Nodes.JsonArray items) return null;
            return (System.Text.Json.Nodes.JsonArray)items.DeepClone();
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }
    public static int? ItemCount(string output) => Items(output)?.Count;

    /// <summary>Written after all guard tuning (2026-11-05) and never tuned against. Holdout above became tuning data once its misses were fixed.</summary>
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

    /// <summary>Written after the holdout 2 fixes (2026-11-05), before any run, and never tuned against.</summary>
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

    /// <summary>Written with the worked examples (2026-11-05), before any run, and never tuned against. Holdout 3 is no longer clean: its misses shaped the examples.</summary>
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

    /// <summary>Written after the round-3 fixes (2026-11-05), before any run, and never tuned against. Holdout 4 is no longer clean.</summary>
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
        System.Text.Json.Nodes.JsonNode? node;
        // A duplicate key ("items" twice) throws ArgumentException, and only on first access to the object, not in Parse.
        System.Text.Json.Nodes.JsonObject? args;
        try { node = System.Text.Json.Nodes.JsonNode.Parse(output); args = (node is System.Text.Json.Nodes.JsonArray { Count: > 0 } a ? a[0] : node)?["arguments"] as System.Text.Json.Nodes.JsonObject; _ = args?.Count; }
        catch (Exception e) when (e is JsonException or ArgumentException) { return output; }
        var call = node is System.Text.Json.Nodes.JsonArray { Count: > 0 } a2 ? a2[0] : node;
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
            var item = new System.Text.Json.Nodes.JsonObject { ["title"] = Str(args, "match") };
            foreach (var k in new[] { "day", "time", "important" }) if (args[k] is { } v) item[k] = v.DeepClone();
            call["name"] = name = "create_reminders";
            call["arguments"] = args = new System.Text.Json.Nodes.JsonObject { ["items"] = new System.Text.Json.Nodes.JsonArray(item) };
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
        static System.Text.Json.Nodes.JsonObject NewItems(System.Text.Json.Nodes.JsonObject item) => new() { ["items"] = new System.Text.Json.Nodes.JsonArray(item) };

        var single = Segments(phrase).Count == 1;
        void Become(string tool, System.Text.Json.Nodes.JsonObject a) { call["name"] = name = tool; call["arguments"] = args = a; }
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
        if (name == "create_reminders" && args["items"] is System.Text.Json.Nodes.JsonArray { Count: 1 } one && one[0] is System.Text.Json.Nodes.JsonObject only)
        {
            if (dayWord is not null && (Str(only, "day") is not { } d1 || !said.Contains(Norm(d1)))) only["day"] = dayWord;
            if (only["time"] is null && timeWord is not null) only["time"] = timeWord;
            if (Lead(said) is { } lead && !(Str(only, "title") is { } tt && Norm(tt).Contains(lead.Split(' ')[0]))) only["title"] = lead;
        }

        var hourUses = new Dictionary<string, int>();
        void Clean(System.Text.Json.Nodes.JsonObject o)
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
        if (args["items"] is System.Text.Json.Nodes.JsonArray items)
        {
            foreach (var i in items.OfType<System.Text.Json.Nodes.JsonObject>()) Clean(i);
            foreach (var i in items.OfType<System.Text.Json.Nodes.JsonObject>().ToList())
                if (i["title"]?.GetValue<string>() is not { } title || !Regex.Split(Norm(title), @"[^\p{L}\p{N}]+").Any(w => w.Length > 2 && words.Any(x => x.StartsWith(w.TrimEnd('s')))))
                    items.Remove(i);
            if (items.Count == 0) { call!["name"] = "none"; call["arguments"] = new System.Text.Json.Nodes.JsonObject(); }
            if (items.Count > 1 && !Regex.IsMatch(said, @"^\s*(?:today|tonight|tomorrow|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\s*:"))
            {
                var itemParts = Regex.Split(said, @",|;|\band\b").Where(x => x.Trim().Length > 0).ToList();
                bool Owns(System.Text.Json.Nodes.JsonObject i, string v) => TitleWords(Str(i, "title")).ToList() is var tw
                    && (tw.Count == 0 || itemParts.Any(pt => Regex.IsMatch(pt, $@"\b{Regex.Escape(v)}") && tw.Any(w => Regex.IsMatch(pt, $@"\b{Regex.Escape(w)}"))));
                var loose = new List<(string Key, string Value, string Said)>();
                foreach (var i in items.OfType<System.Text.Json.Nodes.JsonObject>())
                {
                    if (Str(i, "day") is { } idy && !Owns(i, Norm(idy))) { i.Remove("day"); loose.Add(("day", idy, Norm(idy))); }
                    if (Str(i, "time") is { } itm && Regex.Match(itm, @"\d{1,2}") is { Success: true } th && !Owns(i, th.Value.TrimStart('0'))) { i.Remove("time"); loose.Add(("time", itm, th.Value.TrimStart('0'))); }
                }
                // A day or time stuck on the wrong item moves to the item whose part said it ("call mom at 6", not "buy milk").
                foreach (var (key, value, word) in loose)
                    if (items.OfType<System.Text.Json.Nodes.JsonObject>().FirstOrDefault(i => i[key] is null && Owns(i, word)) is { } owner) owner[key] = value;
            }
            // Important comes from the words, not the model: the whole phrase for one item, the item's own part of a list for several.
            var marked = said.Contains("important") || phrase.Contains('!');
            var parts = Regex.Split(said, @",|;|\band\b").Where(x => x.Trim().Length > 0).ToList();
            foreach (var i in items.OfType<System.Text.Json.Nodes.JsonObject>())
                i["important"] = items.Count == 1 ? marked
                    : parts.Any(part => (part.Contains("important") || part.Contains('!')) && TitleWords(Str(i, "title")).Any(w => Regex.IsMatch(part, $@"\b{Regex.Escape(w)}")));
        }
        return node!.ToJsonString();
    }

    static string? Str(System.Text.Json.Nodes.JsonObject o, string k) => o[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? s) ? s : null;
    static IEnumerable<string> TitleWords(string? title) =>
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
