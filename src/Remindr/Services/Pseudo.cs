using System.Text;
using System.Text.RegularExpressions;

namespace Remindr.Services;

/// <summary>
/// Pseudo-localization for the Debug-only qps-ploc language: letters become accented look-alikes, the text is padded by about a
/// third with middle dots and wrapped in brackets, so hard-coded English stands out and layouts see realistic expansion.
/// {0}-style placeholders and date/time format patterns are left alone.
/// </summary>
public static class Pseudo
{
    private const string From = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string To   = "áƀçďéƒĝĥíĵķĺḿñóƥʠŕšţúṽẃẍýžÁßÇĎÉƑĜĤÍĴĶĹḾÑÓƤǪŔŠŢÚṼẂẌÝŽ";

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    public static string Transform(string text)
    {
        if (text.Length == 0) return text;
        var sb = new StringBuilder("[");
        var pos = 0;
        foreach (Match m in Placeholder.Matches(text))
        {
            Accent(text.AsSpan(pos, m.Index - pos), sb);
            sb.Append(m.Value);
            pos = m.Index + m.Length;
        }
        Accent(text.AsSpan(pos), sb);
        sb.Append('·', Math.Max(1, text.Length * 35 / 100));
        return sb.Append(']').ToString();
    }

    private static void Accent(ReadOnlySpan<char> s, StringBuilder sb)
    {
        foreach (var c in s)
        {
            var i = From.IndexOf(c);
            sb.Append(i >= 0 ? To[i] : c);
        }
    }
}
