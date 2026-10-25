namespace Remindr.Services;

/// <summary>
/// Builds the GitHub issue links. UI-free and pure: only the app version and an OS description are ever included,
/// never a user name, machine name or file path. Field ids must match .github/ISSUE_TEMPLATE/bug_report.yml.
/// </summary>
public static class FeedbackLinks
{
    public const string BugTemplate = "bug_report.yml";
    public const string FeatureTemplate = "feature_request.yml";
    public const string VersionFieldId = "app-version";
    public const string WindowsFieldId = "windows";

    public static string BugReportUrl(string version, string osText) =>
        $"{RepoInfo.IssuesUrl}/new?template={BugTemplate}" +
        $"&{VersionFieldId}={Uri.EscapeDataString(version)}" +
        $"&{WindowsFieldId}={Uri.EscapeDataString(osText)}";

    public static string FeatureRequestUrl() =>
        $"{RepoInfo.IssuesUrl}/new?template={FeatureTemplate}";

    public const string TranslationTemplate = "translation.yml";
    public const string LanguageFieldId = "language";

    /// <summary>Translation-fix form, with the language the person is using filled in (its English name, e.g. "German (de)").</summary>
    public static string TranslationUrl(string languageCode)
    {
        var info = LocalizationService.Find(languageCode);
        var text = info is null ? languageCode : $"{info.EnglishName} ({info.Code})";
        return $"{RepoInfo.IssuesUrl}/new?template={TranslationTemplate}&{LanguageFieldId}={Uri.EscapeDataString(text)}";
    }

    /// <summary>E.g. "Windows 11 (10.0.26300)". Build 22000 and up is Windows 11.</summary>
    public static string OsText(Version os)
    {
        var name = os.Major >= 10 && os.Build >= 22000 ? "Windows 11" : os.Major >= 10 ? "Windows 10" : "Windows";
        return $"{name} ({os.Major}.{os.Minor}.{os.Build})";
    }
}
