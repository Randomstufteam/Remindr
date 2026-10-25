namespace Remindr.Services;

/// <summary>
/// Fallback fonts per language, as a comma-separated WPF font-family list (a plain string, so the logic stays UI-free). The UI keeps its
/// normal Latin font first, exactly as before; these come after it and are used only for characters it lacks.
/// Han characters are shared by Japanese, Chinese and Korean, so each of those puts its own font first (otherwise a Japanese kanji
/// would be drawn in the Chinese form), then broad coverage of the remaining scripts follows.
/// </summary>
public static class FontPolicy
{
    private const string Common = "Segoe UI, Nirmala UI, Leelawadee UI, Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI, Malgun Gothic, Segoe UI Symbol";

    public static string FallbacksFor(string? languageCode) => languageCode switch
    {
        "ja" => "Yu Gothic UI, Meiryo UI, " + Common,
        "ko" => "Malgun Gothic, " + Common,
        "zh-Hans" => "Microsoft YaHei UI, " + Common,
        "zh-Hant" => "Microsoft JhengHei UI, " + Common,
        "th" => "Leelawadee UI, " + Common,
        "hi" or "bn" => "Nirmala UI, " + Common,
        _ => Common,
    };

    /// <summary>The full list: the UI's own font first, then the language's fallbacks.</summary>
    public static string Chain(string baseFont, string? languageCode) => baseFont + ", " + FallbacksFor(languageCode);
}
