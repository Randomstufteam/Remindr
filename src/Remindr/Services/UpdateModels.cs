using System.Globalization;
using System.Text.Json;

namespace Remindr.Services;

/// <summary>A semantic version (<c>0.10.2</c>, <c>v1.0.0-beta.1</c>). Compared by number, a pre-release sorts before its release.</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string Pre) : IComparable<SemVer>
{
    /// <summary>Accepts an optional leading v, 1 to 3 numeric parts, an optional -pre-release and ignores +build metadata.</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var pre = "";
        var dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; if (pre.Length == 0) return false; }

        var parts = s.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var n = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        version = new SemVer(n[0], n[1], n[2], pre);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Pre.Length == 0 || other.Pre.Length == 0)
            return Pre.Length == other.Pre.Length ? 0 : Pre.Length == 0 ? 1 : -1;

        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var an = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x);
            var bn = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y);
            c = an && bn ? x.CompareTo(y) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(SemVer l, SemVer r) => l.CompareTo(r) > 0;
    public static bool operator <(SemVer l, SemVer r) => l.CompareTo(r) < 0;
    public static bool operator >=(SemVer l, SemVer r) => l.CompareTo(r) >= 0;
    public static bool operator <=(SemVer l, SemVer r) => l.CompareTo(r) <= 0;

    /// <summary>Display text without the v prefix, e.g. 0.4.0.</summary>
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");
}

/// <summary>What the update check needs from a GitHub release.</summary>
public sealed record ReleaseInfo(SemVer Version, string Tag, string HtmlUrl, string? InstallerUrl, string? ChecksumUrl);

/// <summary>Reads the <c>releases/latest</c> JSON. Pure, no network.</summary>
public static class ReleaseParser
{
    public const string InstallerAssetName = "Remindr-Installer.exe";
    public const string ChecksumAssetName = "Remindr-Installer.exe.sha256";

    /// <summary>Null when the text is not a usable release: bad JSON, a draft, a pre-release or a tag that is not a version.</summary>
    public static ReleaseInfo? Parse(string json, UpdatePolicy policy)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            var tag = Str(root, "tag_name");
            if (!SemVer.TryParse(tag, out var version) || version.Pre.Length > 0) return null;

            var html = Str(root, "html_url");
            if (html is null || !Uri.TryCreate(html, UriKind.Absolute, out var htmlUri) || !policy.IsPageUrl(htmlUri))
                html = RepoInfo.RepoUrl + "/releases/tag/" + Uri.EscapeDataString(tag!);

            string? installer = null, checksum = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (name == InstallerAssetName) installer ??= url;
                    else if (name == ChecksumAssetName) checksum ??= url;
                }
            return new ReleaseInfo(version, tag!, html, installer, checksum);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The expected hash from a sha256sum line ("&lt;hex&gt;  Remindr-Installer.exe"), lower-case, or null if it is not one.</summary>
    public static string? ParseChecksum(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 64) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var rest = line[64..].Trim().TrimStart('*');
            if (rest.Length == 0 || rest == InstallerAssetName) return hash.ToLowerInvariant();
        }
        return null;
    }
}

/// <summary>
/// Which URLs the updater may contact. https only, on GitHub's own hosts; the installer must come from this repository's
/// releases. Redirect targets may also be GitHub's release storage hosts. In Debug builds a loopback http address can be allowed
/// for testing with a local fake feed.
/// </summary>
public sealed class UpdatePolicy
{
    public static readonly string[] Hosts =
    {
        "github.com", "api.github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    };

    private readonly string _owner, _repo;

    public UpdatePolicy(string owner = RepoInfo.Owner, string repo = RepoInfo.Repo, bool allowLoopback = false)
    {
        _owner = owner;
        _repo = repo;
        AllowLoopback = allowLoopback;
    }

    /// <summary>Debug testing only: plain http to 127.0.0.1 / localhost is accepted for every kind of URL.</summary>
    public bool AllowLoopback { get; }

    private bool IsLoopback(Uri u) =>
        AllowLoopback && u.Scheme == Uri.UriSchemeHttp && u.UserInfo.Length == 0
        && (u.Host == "127.0.0.1" || u.Host == "localhost" || u.Host == "[::1]" || u.Host == "::1");

    private static bool IsGitHubHttps(Uri u, string host) =>
        u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && u.UserInfo.Length == 0
        && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedHost(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && u.UserInfo.Length == 0
        && Hosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>The API call for this repository's latest release.</summary>
    public bool IsFeedUrl(Uri u) =>
        IsLoopback(u) || (AllowLoopback && u.IsFile)
        || (IsGitHubHttps(u, "api.github.com")
            && string.Equals(u.AbsolutePath, $"/repos/{_owner}/{_repo}/releases/latest", StringComparison.OrdinalIgnoreCase));

    /// <summary>A download link of this repository's releases (the installer and its checksum).</summary>
    public bool IsRepoAssetUrl(Uri u) =>
        IsLoopback(u)
        || (IsGitHubHttps(u, "github.com")
            && u.AbsolutePath.StartsWith($"/{_owner}/{_repo}/releases/download/", StringComparison.OrdinalIgnoreCase));

    /// <summary>A hop of a redirect: any allow-listed GitHub host over https.</summary>
    public bool IsRedirectTarget(Uri u) => IsLoopback(u) || IsAllowedHost(u);

    /// <summary>The release web page that "What's new" opens.</summary>
    public bool IsPageUrl(Uri u) =>
        IsGitHubHttps(u, "github.com")
        && u.AbsolutePath.StartsWith($"/{_owner}/{_repo}/", StringComparison.OrdinalIgnoreCase);
}
