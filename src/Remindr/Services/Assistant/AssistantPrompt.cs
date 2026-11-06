namespace Remindr.Services.Assistant;

/// <summary>
/// The model's instructions (ported from tools/Remindr.AiSpike): LFM2.5's own tool format, ten worked examples shown as earlier turns,
/// and a per-tool grammar so the answer is always a parseable call. English only. The model only extracts fields (title, day words,
/// time words, important); C# resolves every date, so the prompt never carries today's date.
/// </summary>
public static class AssistantPrompt
{
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
    public static readonly string SystemPrompt = SystemPromptRaw.Replace("@DAY@", Day).Replace("@TIME@", Time).Replace("@MATCH@", Match);

    /// <summary>Chat mode: a plain, short, friendly chat. The model has no tools here and cannot change reminders.</summary>
    public const string ChatSystemPrompt =
        "You are the friendly assistant inside Remindr, a reminders app. Chat briefly: one to three short sentences. " +
        "In this chat you cannot see or change the user's reminders. If they want a reminder added, changed or deleted, tell them to switch to Work mode.";

    /// <summary>Worked examples shown to the model before each message, as user/assistant turns.</summary>
    public static readonly (string User, string Call)[] Shots =
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

    /// <summary>The model's own call shape, [{"name": tool, "arguments": {...}}], and nothing after it. Each tool's required field comes first and can't be empty: create needs items with a title, the others a match.</summary>
    public const string Gbnf = """
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
}
