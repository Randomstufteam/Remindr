using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;

namespace Remindr.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<Uri> Requests { get; } = new();
    public List<HttpRequestMessage> Sent { get; } = new();

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_respond(request));
    }

    public static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    public static HttpResponseMessage Ok(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    public static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };
}

public class SemVerTests
{
    [Theory]
    [InlineData("v0.4.0", "0.3.0", true)]
    [InlineData("0.4.0", "0.3.0", true)]
    [InlineData("v0.3.0", "0.3.0", false)]      // equal: not newer
    [InlineData("v0.2.9", "0.3.0", false)]      // older
    [InlineData("v0.10.0", "0.9.0", true)]      // numeric, not text, compare
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("v1.0.0", "1.0.0-beta.1", true)] // a release beats its pre-release
    [InlineData("v1.0.0-beta.2", "1.0.0-beta.10", false)]
    public void IsNewer(string latest, string current, bool expected)
    {
        Assert.True(SemVer.TryParse(latest, out var l));
        Assert.True(SemVer.TryParse(current, out var c));
        Assert.Equal(expected, l > c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.x")]
    [InlineData("1.2.3.4")]
    [InlineData("v-1")]
    public void NotAVersion(string text) => Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void ToString_DropsPrefixAndBuild() =>
        Assert.Equal("0.4.0", SemVer.TryParse("v0.4+abc", out var v) ? v.ToString() : "");
}

public class ReleaseParserTests
{
    private static readonly UpdatePolicy Policy = new();

    private static string Json(string tag = "v0.4.0", bool draft = false, bool pre = false, string assets = "") =>
        $$"""
        {"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},
         "html_url":"https://github.com/Randomstufteam/Remindr/releases/tag/{{tag}}","assets":[{{assets}}]}
        """;

    private const string Installer =
        """{"name":"Remindr-Installer.exe","browser_download_url":"https://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/Remindr-Installer.exe"}""";
    private const string Checksum =
        """{"name":"Remindr-Installer.exe.sha256","browser_download_url":"https://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/Remindr-Installer.exe.sha256"}""";
    private const string Other =
        """{"name":"notes.txt","browser_download_url":"https://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/notes.txt"}""";

    [Fact]
    public void Parses_Tag_Url_AndPicksTheRightAssets()
    {
        var r = ReleaseParser.Parse(Json(assets: Other + "," + Checksum + "," + Installer), Policy);
        Assert.NotNull(r);
        Assert.Equal("0.4.0", r!.Version.ToString());
        Assert.Equal("v0.4.0", r.Tag);
        Assert.EndsWith("/releases/tag/v0.4.0", r.HtmlUrl);
        Assert.EndsWith("/Remindr-Installer.exe", r.InstallerUrl);
        Assert.EndsWith("/Remindr-Installer.exe.sha256", r.ChecksumUrl);
    }

    [Fact]
    public void MissingAssets_AreNull()
    {
        var r = ReleaseParser.Parse(Json(assets: Other), Policy);
        Assert.NotNull(r);
        Assert.Null(r!.InstallerUrl);
        Assert.Null(r.ChecksumUrl);
    }

    [Fact]
    public void Draft_Prerelease_AndBadTags_AreIgnored()
    {
        Assert.Null(ReleaseParser.Parse(Json(draft: true), Policy));
        Assert.Null(ReleaseParser.Parse(Json(pre: true), Policy));
        Assert.Null(ReleaseParser.Parse(Json(tag: "v1.0.0-rc.1"), Policy));
        Assert.Null(ReleaseParser.Parse(Json(tag: "nightly"), Policy));
        Assert.Null(ReleaseParser.Parse("not json", Policy));
        Assert.Null(ReleaseParser.Parse("[]", Policy));
    }

    [Fact]
    public void ForeignHtmlUrl_FallsBackToThisRepository()
    {
        var json = Json().Replace("https://github.com/Randomstufteam/Remindr/releases/tag/v0.4.0", "https://evil.example/x");
        Assert.StartsWith(RepoInfo.RepoUrl, ReleaseParser.Parse(json, Policy)!.HtmlUrl);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  Remindr-Installer.exe\n", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef *Remindr-Installer.exe", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  other.exe", false)]
    [InlineData("xyz", false)]
    [InlineData("", false)]
    public void ParseChecksum(string text, bool ok) => Assert.Equal(ok, ReleaseParser.ParseChecksum(text) is not null);
}

public class UpdatePolicyTests
{
    private static readonly UpdatePolicy P = new();
    private static Uri U(string s) => new(s);

    [Fact]
    public void Feed_MustBeThisReposLatestRelease_OnTheApiHost()
    {
        Assert.True(P.IsFeedUrl(U("https://api.github.com/repos/Randomstufteam/Remindr/releases/latest")));
        Assert.False(P.IsFeedUrl(U("http://api.github.com/repos/Randomstufteam/Remindr/releases/latest")));
        Assert.False(P.IsFeedUrl(U("https://api.github.com/repos/someone/else/releases/latest")));
        Assert.False(P.IsFeedUrl(U("https://github.com/repos/Randomstufteam/Remindr/releases/latest")));
        Assert.False(P.IsFeedUrl(U("https://api.github.com.evil.example/repos/Randomstufteam/Remindr/releases/latest")));
    }

    [Fact]
    public void Assets_MustBeThisRepositorysReleaseDownloads()
    {
        Assert.True(P.IsRepoAssetUrl(U("https://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/Remindr-Installer.exe")));
        Assert.False(P.IsRepoAssetUrl(U("http://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/Remindr-Installer.exe")));
        Assert.False(P.IsRepoAssetUrl(U("https://github.com/other/Remindr/releases/download/v0.4.0/Remindr-Installer.exe")));
        Assert.False(P.IsRepoAssetUrl(U("https://github.com/Randomstufteam/Remindr/archive/main.zip")));
        Assert.False(P.IsRepoAssetUrl(U("https://objects.githubusercontent.com/Randomstufteam/Remindr/releases/download/x")));
        Assert.False(P.IsRepoAssetUrl(U("https://github.com:8443/Randomstufteam/Remindr/releases/download/v1/x")));
        Assert.False(P.IsRepoAssetUrl(U("https://user:pw@github.com/Randomstufteam/Remindr/releases/download/v1/x")));
        Assert.False(P.IsRepoAssetUrl(U("https://github.com/Randomstufteam/Remindr/releases/download/../../../evil/x"))); // normalised away
    }

    [Theory]
    [InlineData("https://github.com/x", true)]
    [InlineData("https://api.github.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("http://release-assets.githubusercontent.com/x", false)]
    [InlineData("https://githubusercontent.com/x", false)]
    [InlineData("https://raw.githubusercontent.com/x", false)]
    [InlineData("https://evil.example/x", false)]
    [InlineData("http://127.0.0.1:5000/x", false)]
    public void RedirectTargets_AreAllowListedHttpsHosts(string url, bool ok) => Assert.Equal(ok, P.IsRedirectTarget(U(url)));

    [Fact]
    public void Loopback_IsOnlyAllowedWhenSwitchedOn()
    {
        var on = new UpdatePolicy(allowLoopback: true);
        Assert.True(on.IsRedirectTarget(U("http://127.0.0.1:5000/x")));
        Assert.True(on.IsFeedUrl(U("http://localhost:5000/feed.json")));
        Assert.False(on.IsRedirectTarget(U("http://192.168.1.5/x")));
        Assert.False(P.IsFeedUrl(U("http://127.0.0.1:5000/feed.json")));
    }
}

public class HttpUpdateClientTests
{
    private const string Asset = "https://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/Remindr-Installer.exe";

    [Fact]
    public async Task SendsUserAgent_NoAuth_AndFollowsGitHubRedirects()
    {
        var h = new FakeHandler(r => r.RequestUri!.Host == "github.com"
            ? FakeHandler.Redirect("https://release-assets.githubusercontent.com/blob/1")
            : FakeHandler.Ok(new byte[] { 1, 2, 3 }));
        var client = new HttpUpdateClient(h, new UpdatePolicy(), "Remindr/0.3.0");
        using var ms = new MemoryStream();
        await client.DownloadAsync(new Uri(Asset), ms, null, 1000, default);
        Assert.Equal(new byte[] { 1, 2, 3 }, ms.ToArray());
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task UserAgentAndNoAuthorization_AreSent()
    {
        HttpRequestMessage? seen = null;
        var h = new FakeHandler(r => { seen = r; return FakeHandler.Ok("{}"); });
        var client = new HttpUpdateClient(h, new UpdatePolicy(), "Remindr/0.3.0");
        await client.GetStringAsync(new Uri("https://api.github.com/repos/Randomstufteam/Remindr/releases/latest"), default);
        Assert.Contains("Remindr/0.3.0", seen!.Headers.UserAgent.ToString());
        Assert.Null(seen.Headers.Authorization);
    }

    [Fact]
    public async Task RedirectToAnotherHost_IsRefused()
    {
        var h = new FakeHandler(_ => FakeHandler.Redirect("https://evil.example/Remindr-Installer.exe"));
        var client = new HttpUpdateClient(h, new UpdatePolicy(), "Remindr/0.3.0");
        await Assert.ThrowsAsync<UpdateSecurityException>(() => client.DownloadAsync(new Uri(Asset), new MemoryStream(), null, 1000, default));
        Assert.Single(h.Requests); // never contacted the foreign host
    }

    [Fact]
    public async Task RedirectDowngradeToHttp_IsRefused()
    {
        var h = new FakeHandler(_ => FakeHandler.Redirect("http://objects.githubusercontent.com/x"));
        var client = new HttpUpdateClient(h, new UpdatePolicy(), "Remindr/0.3.0");
        await Assert.ThrowsAsync<UpdateSecurityException>(() => client.GetStringAsync(new Uri(Asset), default));
    }

    [Fact]
    public async Task Download_PastTheSizeLimit_Fails()
    {
        var h = new FakeHandler(_ => FakeHandler.Ok(new byte[5000]));
        var client = new HttpUpdateClient(h, new UpdatePolicy(), "Remindr/0.3.0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(new Uri(Asset), new MemoryStream(), null, 1000, default));
    }

    [Fact]
    public async Task ServerError_Throws()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)); // e.g. rate limited
        var client = new HttpUpdateClient(h, new UpdatePolicy(), "Remindr/0.3.0");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync(new Uri(Asset), default));
    }
}

public class UpdateServiceTests : IDisposable
{
    private const string Base = "https://github.com/Randomstufteam/Remindr/releases/download/v0.4.0/";
    private readonly TempDb _db = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "remindr-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsRepo _settings;
    private List<string> Logged { get; } = new();

    public UpdateServiceTests()
    {
        new DbInitializer(_db.Db).Initialize();
        _settings = new SettingsRepo(_db.Db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_temp, true); } catch { }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime Now => new(2026, 10, 25, 21, 41, 0);
        public DateTime UtcNow => Now.ToUniversalTime();
    }

    private sealed class RecordingLauncher : IInstallerLauncher
    {
        public List<string> Launched { get; } = new();
        public bool Launch(string path) { Launched.Add(path); return true; }
    }

    private static string ReleaseJson(string tag = "v0.4.0", bool pre = false, string? installer = Base + "Remindr-Installer.exe",
        string? checksum = Base + "Remindr-Installer.exe.sha256")
    {
        var assets = new List<string>();
        if (installer is not null) assets.Add($$"""{"name":"Remindr-Installer.exe","browser_download_url":"{{installer}}"}""");
        if (checksum is not null) assets.Add($$"""{"name":"Remindr-Installer.exe.sha256","browser_download_url":"{{checksum}}"}""");
        return $$"""{"tag_name":"{{tag}}","draft":false,"prerelease":{{(pre ? "true" : "false")}},"html_url":"https://github.com/Randomstufteam/Remindr/releases/tag/{{tag}}","assets":[{{string.Join(",", assets)}}]}""";
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private (UpdateService svc, FakeHandler handler, RecordingLauncher launcher) Make(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string current = "0.3.0")
    {
        var handler = new FakeHandler(respond);
        var policy = new UpdatePolicy();
        var launcher = new RecordingLauncher();
        var svc = new UpdateService(new HttpUpdateClient(handler, policy, "Remindr/" + current), _settings, policy, launcher,
            new FixedClock(), current, _temp, null, (_, context) => Logged.Add(context)); // never the real error.log
        return (svc, handler, launcher);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Server(string releaseJson, byte[] installer, string? hashOverride = null) => r =>
    {
        var url = r.RequestUri!.ToString();
        if (url.EndsWith("/releases/latest")) return FakeHandler.Ok(releaseJson);
        if (url.EndsWith(".sha256")) return FakeHandler.Ok((hashOverride ?? Sha(installer)) + "  Remindr-Installer.exe\n");
        if (url.EndsWith("Remindr-Installer.exe")) return FakeHandler.Ok(installer);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    };

    // ---- the check ----

    [Fact]
    public async Task NewerRelease_ShowsTheBanner_AndUsesTheApiUrlAndUserAgent()
    {
        var (svc, h, _) = Make(Server(ReleaseJson(), new byte[10]));
        await svc.CheckAsync(manual: false);
        Assert.Equal(UpdateState.Available, svc.State);
        Assert.True(svc.ShowBanner);
        Assert.Equal("0.4.0", svc.Release!.Version.ToString());
        Assert.Equal("https://api.github.com/repos/Randomstufteam/Remindr/releases/latest", h.Requests.Single().ToString());
        Assert.NotNull(svc.LastChecked);
    }

    [Fact]
    public async Task SameOrOlderRelease_ShowsNoBanner()
    {
        var (svc, _, _) = Make(Server(ReleaseJson("v0.3.0"), new byte[10]));
        await svc.CheckAsync(manual: false);
        Assert.Equal(UpdateState.UpToDate, svc.State);
        Assert.False(svc.ShowBanner);

        var (older, _, _) = Make(Server(ReleaseJson("v0.2.0"), new byte[10]));
        await older.CheckAsync(manual: false);
        Assert.False(older.ShowBanner);
    }

    [Fact]
    public async Task Prerelease_IsIgnored()
    {
        var (svc, _, _) = Make(Server(ReleaseJson("v0.5.0", pre: true), new byte[10]));
        await svc.CheckAsync(manual: false);
        Assert.False(svc.ShowBanner);
    }

    [Fact]
    public async Task Disabled_MakesNoRequestAtStartup()
    {
        _settings.Set(UpdateService.EnabledKey, "0");
        var (svc, h, _) = Make(Server(ReleaseJson(), new byte[10]));
        await svc.RunStartupCheckAsync();
        Assert.Empty(h.Requests);
        Assert.False(svc.ShowBanner);
        Assert.False(svc.CheckEnabled);
    }

    [Fact]
    public async Task Enabled_ByDefault_AndStartupCheckRuns()
    {
        var (svc, h, _) = Make(Server(ReleaseJson(), new byte[10]));
        Assert.True(svc.CheckEnabled);
        await svc.RunStartupCheckAsync();
        Assert.Single(h.Requests);
        Assert.True(svc.ShowBanner);
    }

    [Fact]
    public async Task Offline_Timeout_AndRateLimit_AreSilent()
    {
        foreach (Func<HttpRequestMessage, HttpResponseMessage> fail in new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => throw new HttpRequestException("offline"),
            _ => throw new TaskCanceledException("timeout"),
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
            _ => FakeHandler.Ok("<html>captive portal</html>"),
        })
        {
            var (svc, _, _) = Make(fail);
            await svc.RunStartupCheckAsync(); // must not throw
            Assert.False(svc.ShowBanner);
            Assert.NotEqual(UpdateState.Available, svc.State);
        }
    }

    [Fact]
    public async Task RepeatedFailures_AreLoggedOnce()
    {
        var (svc, _, _) = Make(_ => throw new HttpRequestException("offline"));
        await svc.CheckAsync(manual: false);
        await svc.CheckAsync(manual: true);
        Assert.Single(Logged);
    }

    [Fact]
    public async Task Skip_HidesThatVersionOnTheNextLaunch_ButNotANewerOne()
    {
        var (svc, _, _) = Make(Server(ReleaseJson("v0.4.0"), new byte[10]));
        await svc.CheckAsync(manual: false);
        svc.Skip();
        Assert.False(svc.ShowBanner);
        Assert.Equal("0.4.0", _settings.Get(UpdateService.SkippedKey));

        var (next, _, _) = Make(Server(ReleaseJson("v0.4.0"), new byte[10]));  // "next launch"
        await next.CheckAsync(manual: false);
        Assert.False(next.ShowBanner);

        var (newer, _, _) = Make(Server(ReleaseJson("v0.5.0"), new byte[10]));
        await newer.CheckAsync(manual: false);
        Assert.True(newer.ShowBanner);

        // The manual "Check for updates" still reports a skipped version.
        await next.CheckAsync(manual: true);
        Assert.True(next.ShowBanner);
    }

    [Fact]
    public async Task Later_HidesForThisSessionOnly()
    {
        var (svc, _, _) = Make(Server(ReleaseJson(), new byte[10]));
        await svc.CheckAsync(manual: false);
        svc.Later();
        Assert.False(svc.ShowBanner);
        Assert.Null(_settings.Get(UpdateService.SkippedKey));

        var (next, _, _) = Make(Server(ReleaseJson(), new byte[10]));
        await next.CheckAsync(manual: false);
        Assert.True(next.ShowBanner);
    }

    [Fact]
    public async Task CurrentVersion_0_10_IsNewerThan_0_9()
    {
        var (svc, _, _) = Make(Server(ReleaseJson("v0.10.0"), new byte[10]), current: "0.9.0");
        await svc.CheckAsync(manual: false);
        Assert.True(svc.ShowBanner);
    }

    // ---- the download ----

    [Fact]
    public async Task MatchingHash_IsReady_ThenLaunchesTheVerifiedInstaller_FromTheTempFolder()
    {
        var data = Encoding.ASCII.GetBytes("pretend installer");
        var (svc, h, launcher) = Make(Server(ReleaseJson(), data));
        var started = 0;
        svc.InstallerStarted += (_, _) => started++;
        await svc.CheckAsync(manual: false);
        await svc.UpdateNowAsync();

        // Verified, but nothing runs until "Restart and update".
        Assert.Equal(UpdateState.ReadyToInstall, svc.State);
        Assert.Empty(launcher.Launched);
        Assert.Equal(0, started);
        await svc.InstallAsync();

        var path = Path.Combine(_temp, "Remindr-Installer.exe");
        Assert.Equal(path, launcher.Launched.Single());
        Assert.Equal(1, started);
        Assert.Equal(UpdateState.Launching, svc.State);
        Assert.Equal(data, File.ReadAllBytes(path));
        Assert.All(h.Requests.Skip(1), u => Assert.Equal("github.com", u.Host));
    }

    [Fact]
    public async Task FileChangedAfterVerification_IsNotLaunched()
    {
        var (svc, _, launcher) = Make(Server(ReleaseJson(), Encoding.ASCII.GetBytes("good")));
        await svc.CheckAsync(manual: false);
        await svc.UpdateNowAsync();
        File.WriteAllText(Path.Combine(_temp, "Remindr-Installer.exe"), "tampered");
        await svc.InstallAsync();
        Assert.Equal(UpdateState.HashMismatch, svc.State);
        Assert.Empty(launcher.Launched);
        Assert.False(File.Exists(Path.Combine(_temp, "Remindr-Installer.exe")));
    }

    [Fact]
    public async Task Cancel_DeletesThePartialFile_AndOffersTheUpdateAgain()
    {
        UpdateService? svc = null;
        var (s, _, launcher) = Make(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("Remindr-Installer.exe")) svc!.CancelDownload();
            return Server(ReleaseJson(), new byte[100])(r);
        });
        svc = s;
        await svc.CheckAsync(manual: false);
        await svc.UpdateNowAsync();
        Assert.Equal(UpdateState.Available, svc.State);
        Assert.Empty(launcher.Launched);
        Assert.False(File.Exists(Path.Combine(_temp, "Remindr-Installer.exe")));
    }

    [Fact]
    public async Task HashMismatch_DeletesTheFile_ShowsAnError_AndRunsNothing()
    {
        var data = Encoding.ASCII.GetBytes("corrupted installer");
        var (svc, _, launcher) = Make(Server(ReleaseJson(), data, hashOverride: new string('0', 64)));
        await svc.CheckAsync(manual: false);
        await svc.UpdateNowAsync();

        Assert.Equal(UpdateState.HashMismatch, svc.State);
        Assert.True(svc.ShowBanner);
        Assert.Empty(launcher.Launched);
        Assert.False(File.Exists(Path.Combine(_temp, "Remindr-Installer.exe")));
    }

    [Fact]
    public async Task InstallerFromAnotherRepository_IsRefused()
    {
        var json = ReleaseJson(installer: "https://github.com/someone/else/releases/download/v1/Remindr-Installer.exe");
        var (svc, h, launcher) = Make(Server(json, new byte[10]));
        await svc.CheckAsync(manual: false);
        Assert.False(svc.ShowBanner); // never offered
        var before = h.Requests.Count;
        await svc.UpdateNowAsync();

        Assert.NotEqual(UpdateState.Available, svc.State);
        Assert.Equal(before, h.Requests.Count); // nothing was fetched
        Assert.Empty(launcher.Launched);
    }

    [Theory]
    [InlineData(false, true)]  // no .sha256
    [InlineData(true, false)]  // no installer
    [InlineData(false, false)] // no assets at all
    public async Task ReleaseWithoutVerifiableAssets_IsSilent_AndRefusesToInstall(bool withInstaller, bool withChecksum)
    {
        var json = ReleaseJson(installer: withInstaller ? Base + "Remindr-Installer.exe" : null,
            checksum: withChecksum ? Base + "Remindr-Installer.exe.sha256" : null);
        var (svc, _, launcher) = Make(Server(json, new byte[10]));
        await svc.RunStartupCheckAsync();
        Assert.False(svc.ShowBanner);
        Assert.Equal(UpdateState.CheckFailed, svc.State);
        Assert.Single(Logged);
        await svc.UpdateNowAsync();
        Assert.Empty(launcher.Launched);
        Assert.False(File.Exists(Path.Combine(_temp, "Remindr-Installer.exe")));
    }

    [Fact]
    public async Task RepositoryWithoutReleases_404_IsSilent()
    {
        var (svc, _, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await svc.RunStartupCheckAsync();
        Assert.False(svc.ShowBanner);
        Assert.Equal(UpdateState.CheckFailed, svc.State);
        Assert.Single(Logged);
    }

    [Fact]
    public async Task RedirectToAForeignHost_DuringDownload_FailsWithoutLaunching()
    {
        var data = new byte[10];
        var (svc, _, launcher) = Make(r =>
            r.RequestUri!.AbsolutePath.EndsWith("Remindr-Installer.exe") ? FakeHandler.Redirect("https://evil.example/x.exe") : Server(ReleaseJson(), data)(r));
        await svc.CheckAsync(manual: false);
        await svc.UpdateNowAsync();
        Assert.Equal(UpdateState.DownloadFailed, svc.State);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task Progress_ReachesOne_WhenTheSizeIsKnown()
    {
        var data = new byte[200_000];
        new Random(1).NextBytes(data);
        var (svc, _, _) = Make(Server(ReleaseJson(), data));
        await svc.CheckAsync(manual: false);
        await svc.UpdateNowAsync();
        Assert.Equal(1.0, svc.Progress, 3);
    }
}
