using System.Text.RegularExpressions;

namespace Remindr.Services.Assistant;

public enum SpokenAnswer { None, Yes, No }

/// <summary>Text for the voice side: the yes/no answer to a confirm card and the sentences Remindr says. English only, like the assistant. UI-free.</summary>
public static class VoiceText
{
    private static readonly HashSet<string> Yes = new() { "yes", "confirm", "do it" };
    private static readonly HashSet<string> No = new() { "no", "cancel" };
    /// <summary>Replies longer than this are not read aloud.</summary>
    public const int MaxSpokenLength = 200;

    /// <summary>Matches only the exact short answers (case and punctuation ignored): yes / confirm / do it, and no / cancel. Anything else is None.</summary>
    public static SpokenAnswer Answer(string? transcript)
    {
        var t = Regex.Replace((transcript ?? "").ToLowerInvariant(), @"[^\p{L}\p{N} ]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return Yes.Contains(t) ? SpokenAnswer.Yes : No.Contains(t) ? SpokenAnswer.No : SpokenAnswer.None;
    }

    /// <summary>One line of a confirm card, as far as speech needs it.</summary>
    public sealed record Line(ChangeKind Kind, string Title, string When, bool Important);

    /// <summary>The question read out for a confirm card: "Add 'call mom' Friday at 6 PM?". Several changes are counted instead of listed.</summary>
    public static string Confirm(IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0) return "";
        if (lines.Count > 1) return $"Make these {lines.Count} changes?";
        var l = lines[0];
        var when = Regex.Replace(l.When, @"\s*[·•]\s*", ", ").Trim(' ', ',');
        var tail = when.Length > 0 ? " " + when : "";
        var text = l.Kind switch
        {
            ChangeKind.Create => $"Add '{l.Title}'{tail}{(l.Important ? ", marked important" : "")}?",
            ChangeKind.Update => $"Change '{l.Title}'{(tail.Length > 0 ? " to" + tail : "")}?",
            ChangeKind.Complete => $"Mark '{l.Title}' done?",
            ChangeKind.Delete => $"Delete '{l.Title}'?",
            ChangeKind.Snooze => $"Snooze '{l.Title}'{tail}?",
            _ => "",
        };
        return text;
    }

    /// <summary>The text to read aloud, or null when it is empty or too long to be a short reply.</summary>
    public static string? Short(string? text)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) || t.Length > MaxSpokenLength ? null : t;
    }
}
