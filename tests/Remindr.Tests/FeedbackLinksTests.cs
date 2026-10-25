using System.IO;
using Remindr.Services;

namespace Remindr.Tests;

public class FeedbackLinksTests
{
    [Fact]
    public void BugUrl_HasTemplateVersionAndOs()
    {
        var url = FeedbackLinks.BugReportUrl("0.3.0", "Windows 11 (10.0.26300)");
        Assert.StartsWith(RepoInfo.IssuesUrl + "/new?template=bug_report.yml", url);
        Assert.Contains("&app-version=0.3.0", url);
        Assert.Contains("&windows=Windows%2011%20%2810.0.26300%29", url);
    }

    [Fact]
    public void BugUrl_LeaksNothingPersonal()
    {
        var url = FeedbackLinks.BugReportUrl(AppInfo.Version, AppInfo.OsText);
        // Only the query carries data; the repo owner part of the path may coincidentally contain a name.
        var decoded = Uri.UnescapeDataString(url[url.IndexOf('?')..]);
        foreach (var secret in new[] { Environment.UserName, Environment.MachineName })
            Assert.DoesNotContain(secret, decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", decoded);
        Assert.DoesNotContain("Users", decoded);
        Assert.DoesNotContain(":\\", decoded);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), decoded);
    }

    [Fact]
    public void OsText_UsesVersionOnly()
    {
        Assert.Equal("Windows 11 (10.0.26300)", FeedbackLinks.OsText(new Version(10, 0, 26300, 0)));
        Assert.Equal("Windows 10 (10.0.19045)", FeedbackLinks.OsText(new Version(10, 0, 19045, 0)));
    }

    [Fact]
    public void FeatureUrl_UsesFeatureTemplate() =>
        Assert.EndsWith("/new?template=feature_request.yml", FeedbackLinks.FeatureRequestUrl());

    [Fact]
    public void PrefilledFieldIds_ExistInBugTemplate()
    {
        var dir = AppContext.BaseDirectory;
        string? yml = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, ".github", "ISSUE_TEMPLATE", "bug_report.yml");
            if (File.Exists(p)) { yml = File.ReadAllText(p); break; }
        }
        Assert.NotNull(yml);
        Assert.Contains($"id: {FeedbackLinks.VersionFieldId}", yml);
        Assert.Contains($"id: {FeedbackLinks.WindowsFieldId}", yml);
    }

    [Fact]
    public void AppVersion_Is030() => Assert.Equal("0.3.0", AppInfo.Version);

    [Fact]
    public void RepoInfo_HasRealValues()
    {
        Assert.Equal("https://github.com/Randomstufteam/Remindr", RepoInfo.RepoUrl);
        Assert.DoesNotContain("{{", RepoInfo.RepoUrl);
    }
}
