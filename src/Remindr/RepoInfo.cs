namespace Remindr;

/// <summary>
/// The single place for repository identity. Pure constants, no UI. The installer links this same file.
/// Other files that carry the same values (update them together if the owner/repo/name ever changes):
///   LICENSE (copyright name), README.md, CHANGELOG.md, .github/ISSUE_TEMPLATE/config.yml.
/// Run tools/check-placeholders.ps1 to list any {{...}} placeholders still left in those files.
/// </summary>
public static class RepoInfo
{
    public const string Owner = "Randomstufteam";
    public const string Repo = "Remindr";
    public const string CopyrightName = "Randomstufteam";

    public const string RepoUrl = "https://github.com/" + Owner + "/" + Repo;
    public const string IssuesUrl = RepoUrl + "/issues";
    public const string NoticesUrl = RepoUrl + "/blob/main/THIRD-PARTY-NOTICES.md";
    public const string Publisher = Owner;
    public const string Aumid = "Remindr.App";
    public const string License = "MIT";
}
