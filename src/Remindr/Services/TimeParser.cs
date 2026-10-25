using System.Globalization;

namespace Remindr.Services;

/// <summary>Lenient time-of-day parser for the reminder time box: "3pm", "3:30 PM", "15:30", "1530", "9".</summary>
public static class TimeParser
{
    private static readonly string[] Formats =
    {
        "h:mm tt", "h:mmtt", "h tt", "htt", "H:mm", "HH:mm", "H:mm:ss", "Hmm", "HHmm", "%H", "HH"
    };

    public static bool TryParse(string? text, out TimeOnly time, CultureInfo? culture = null)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace(".", "");
        culture ??= CultureInfo.CurrentCulture;

        // The culture's own format first ("t" is what the suggestions use), then the lenient English-style ones.
        if (TimeOnly.TryParseExact(s, "t", culture, DateTimeStyles.AllowWhiteSpaces, out time)) return true;
        if (TimeOnly.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time)) return true;
        return false;
    }
}

