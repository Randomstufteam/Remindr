namespace Remindr.Services;

/// <summary>One word of the welcome greeting, with the language it is written in (so shaping, direction and font are right).</summary>
/// <param name="Word">The greeting as written, e.g. "こんにちは".</param>
/// <param name="Culture">The word's own language tag (an IETF tag such as "ja" or "ar").</param>
/// <param name="IsRtl">True for right-to-left scripts (Arabic, Hebrew).</param>
/// <param name="Code">The matching supported UI language code (for the font fallback chain), e.g. "zh-Hans".</param>
public sealed record Greeting(string Word, string Culture, bool IsRtl, string Code);

/// <summary>
/// Data for the first-run welcome greeting (UI-free): the words, the order they cycle in and a colour theme per word.
/// The words are data, not resources: each is written in its own language, whatever language the app is shown in.
/// </summary>
public static class GreetingCatalog
{
    /// <summary>Time from one word starting to arrive to the next: ~1.3 s hold plus the ~0.95 s overlapping transition (flows continuously).</summary>
    public static readonly TimeSpan WordPeriod = TimeSpan.FromMilliseconds(2250);

    private static readonly Greeting[] Words =
    {
        new("Hello", "en", false, "en"),
        new("Hola", "es", false, "es"),
        new("Bonjour", "fr", false, "fr"),
        new("Hallo", "de", false, "de"),
        new("Ciao", "it", false, "it"),
        new("Olá", "pt-BR", false, "pt-BR"),
        new("こんにちは", "ja", false, "ja"),
        new("안녕하세요", "ko", false, "ko"),
        new("你好", "zh-Hans", false, "zh-Hans"),
        new("مرحبا", "ar", true, "ar"),
        new("שלום", "he", true, "he"),
        new("नमस्ते", "hi", false, "hi"),
        new("Привет", "ru", false, "ru"),
        new("Merhaba", "tr", false, "tr"),
        new("Γεια σας", "el", false, "el"),
        new("Hej", "sv", false, "sv"),
        new("Cześć", "pl", false, "pl"),
        new("Xin chào", "vi", false, "vi"),
        new("สวัสดี", "th", false, "th"),
        new("Halo", "id", false, "id"),
        new("Kumusta", "fil", false, "fil"),
    };

    /// <summary>All greetings in their fixed order (English first).</summary>
    public static IReadOnlyList<Greeting> All => Words;

    /// <summary>
    /// The cycle for a UI language: the same words, rotated so the word for that language comes first (matched on the base language,
    /// so pt-PT starts with Olá and nb falls back to English's "Hello" when there is no word). The list is never shortened.
    /// </summary>
    public static IReadOnlyList<Greeting> OrderedFor(string? uiLanguageCode)
    {
        var start = IndexFor(uiLanguageCode);
        var list = new List<Greeting>(Words.Length);
        for (var i = 0; i < Words.Length; i++) list.Add(Words[(start + i) % Words.Length]);
        return list;
    }

    private static int IndexFor(string? code)
    {
        var resolved = LocalizationService.ResolveSupported(code);
        if (resolved is null) return 0;
        for (var i = 0; i < Words.Length; i++)
            if (string.Equals(Words[i].Code, resolved, StringComparison.OrdinalIgnoreCase)) return i;
        // pt-PT and friends: match on the language part.
        var lang = resolved.Split('-')[0];
        for (var i = 0; i < Words.Length; i++)
            if (string.Equals(Words[i].Code.Split('-')[0], lang, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>
    /// Four blob colours for the word at <paramref name="index"/>: one hue per word (golden-angle steps so neighbours always differ clearly),
    /// the four blobs spread around it. Returned as RGB triples so this stays UI-free.
    /// </summary>
    public static IReadOnlyList<(byte R, byte G, byte B)> BlobColors(int index)
    {
        var baseHue = (215 + index * 47) % 360;
        double[] offsets = { 0, 38, 150, 215 };
        var list = new List<(byte, byte, byte)>(offsets.Length);
        foreach (var o in offsets) list.Add(FromHsl((baseHue + o) % 360, 0.78, 0.56));
        return list;
    }

    /// <summary>Hue (0-360) of the first blob for the word at <paramref name="index"/> (used by tests and the colour maths).</summary>
    public static double HueFor(int index) => (215 + index * 47) % 360;

    public static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var hp = h / 60.0;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        var m = l - c / 2;
        byte To(double v) => (byte)Math.Round(Math.Clamp(v + m, 0, 1) * 255);
        return (To(r), To(g), To(b));
    }
}
