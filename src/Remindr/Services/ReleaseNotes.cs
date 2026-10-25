using System.Text.RegularExpressions;

namespace Remindr.Services;

public enum NoteLineKind { Heading, Bullet, Text }

public sealed record NoteLine(NoteLineKind Kind, string Text);

/// <summary>
/// Turns a GitHub release body (Markdown) into plain lines for the in-app release notes: headings, bullets and text. No Markdown
/// rendering and nothing remote: images, HTML, links (kept as their text), emphasis and code marks are removed. Pure.
/// </summary>
public static class ReleaseNotes
{
    public const int MaxLines = 60;
    public const int MaxLineLength = 300;
    private const int MaxRawLineLength = 2000;

    private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Html = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Emphasis = new(@"(\*\*|__|~~|`)", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*(?:[-*+]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<NoteLine> Parse(string? body)
    {
        var lines = new List<NoteLine>();
        if (string.IsNullOrWhiteSpace(body)) return lines;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (lines.Count >= MaxLines) break;
            // Cap the raw line before any regex runs: a huge unbroken line (e.g. thousands of '[') would otherwise make the link and
            // HTML patterns backtrack for seconds on the UI thread.
            var line = (raw.Length > MaxRawLineLength ? raw[..MaxRawLineLength] : raw).TrimEnd();
            if (line.Trim().Length == 0 || Regex.IsMatch(line, @"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$")) continue;

            NoteLineKind kind;
            string text;
            if (Heading.Match(line) is { Success: true } h) { kind = NoteLineKind.Heading; text = h.Groups[1].Value; }
            else if (Bullet.Match(line) is { Success: true } b) { kind = NoteLineKind.Bullet; text = b.Groups[1].Value; }
            else { kind = NoteLineKind.Text; text = line; }

            text = Clean(text);
            if (text.Length == 0) continue;
            if (text.Length > MaxLineLength) text = text[..MaxLineLength].TrimEnd() + "…";
            lines.Add(new NoteLine(kind, text));
        }
        return lines;
    }

    private static string Clean(string text)
    {
        text = Image.Replace(text, "");
        text = Link.Replace(text, "$1");
        text = Html.Replace(text, "");
        text = Emphasis.Replace(text, "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}
