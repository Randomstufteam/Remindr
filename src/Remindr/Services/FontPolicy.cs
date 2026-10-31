namespace Remindr.Services;

/// <summary>
/// Fallback fonts per language, as a comma-separated WPF font-family list (a plain string, so the logic stays UI-free). The UI keeps its
/// normal Latin font first, exactly as before; these come after it and are used only for characters it lacks.
/// Han characters are shared by Japanese, Chinese and Korean, so each of those puts its own font first (otherwise a Japanese kanji
/// would be drawn in the Chinese form), then broad coverage of the remaining scripts follows.
/// One chain per script group: the fonts that ship with every Windows 10/11 come first (Nirmala UI, Ebrima, Gadugi, Myanmar Text,
/// Microsoft Himalaya, MV Boli, Leelawadee UI) and the Features-on-Demand fonts (Khmer UI, Lao UI) come last,
/// so a PC without language packs still draws every script.
/// </summary>
public static class FontPolicy
{
    private const string Common = "Segoe UI, Nirmala UI, Leelawadee UI, Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI, Malgun Gothic, Segoe UI Symbol";

    public static string FallbacksFor(string? languageCode) => ScriptGroupFont(languageCode) is { Length: > 0 } first ? first + ", " + Common : Common;

    /// <summary>The fonts for a language's own script, ahead of <see cref="Common"/> (empty when the common chain already starts right).</summary>
    private static string ScriptGroupFont(string? code) => code switch
    {
        // Han: each language's own form first.
        "ja" => "Yu Gothic UI, Meiryo UI",
        "ko" => "Malgun Gothic",
        "zh-Hans" => "Microsoft YaHei UI",
        "zh-Hant" => "Microsoft JhengHei UI",
        // Thai, Lao, Khmer, Myanmar, Tibetan.
        "th" => "Leelawadee UI",
        "lo" => "Leelawadee UI, Lao UI",
        "km" => "Leelawadee UI, Khmer UI",
        "my" => "Myanmar Text",
        "bo" or "dz" => "Microsoft Himalaya",
        // Indic scripts: Nirmala UI covers Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada, Malayalam, Sinhala and Ol Chiki.
        "hi" or "bn" or "mr" or "ne" or "sa" or "kok" or "mai" or "brx" or "doi" or "as" or "mni-Beng" or "pa" or "gu" or "or"
            or "ta" or "te" or "kn" or "ml" or "si" or "sat-Olck" => "Nirmala UI",
        // Ethiopic, Tifinagh.
        "am" or "ti" or "zgh" => "Ebrima",
        // Thaana.
        "dv" => "MV Boli",
        // Arabic and Hebrew scripts: Segoe UI covers them (Urdu, Persian, Pashto, Sindhi, Uyghur, Kurdish, Punjabi, Kashmiri, Yiddish).
        "ar" or "he" or "fa" or "ur" or "ps" or "sd-Arab" or "ug" or "ckb" or "pa-Arab" or "ks-Arab" or "yi" => "Segoe UI",
        _ => "",
    };

    /// <summary>The full list: the UI's own font first, then the language's fallbacks.</summary>
    public static string Chain(string baseFont, string? languageCode) => baseFont + ", " + FallbacksFor(languageCode);
}
