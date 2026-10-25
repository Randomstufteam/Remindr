using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>Raised when a URL is refused by <see cref="UpdatePolicy"/> (wrong host, not https, not this repository).</summary>
public sealed class UpdateSecurityException : Exception
{
    public UpdateSecurityException(string message) : base(message) { }
}

/// <summary>The two network operations the updater needs. A fake implementation (or a fake HttpMessageHandler) is used in tests.</summary>
public interface IUpdateClient
{
    Task<string> GetStringAsync(Uri url, CancellationToken ct);

    /// <summary>Streams the body into <paramref name="destination"/>, reporting 0..1 when the size is known. Stops with an error past <paramref name="maxBytes"/>.</summary>
    Task DownloadAsync(Uri url, Stream destination, Action<double>? progress, long maxBytes, CancellationToken ct);
}

/// <summary>
/// HttpClient-based client. Redirects are followed by hand so every hop is checked against the allow-list (GitHub answers an
/// asset download with a redirect to its release storage host). No cookies, no credentials, only a User-Agent.
/// </summary>
public sealed class HttpUpdateClient : IUpdateClient
{
    private const int MaxHops = 5;
    private readonly HttpClient _http;
    private readonly UpdatePolicy _policy;
    private readonly TimeSpan _textTimeout;

    /// <param name="handler">The message handler. It must not follow redirects itself (see <see cref="CreateHandler"/>).</param>
    public HttpUpdateClient(HttpMessageHandler handler, UpdatePolicy policy, string userAgent, TimeSpan? textTimeout = null)
    {
        _policy = policy;
        _textTimeout = textTimeout ?? TimeSpan.FromSeconds(5);
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static HttpMessageHandler CreateHandler() =>
        new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true };

    public async Task<string> GetStringAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_textTimeout);
        using var response = await SendAsync(url, timeout.Token);
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    public async Task DownloadAsync(Uri url, Stream destination, Action<double>? progress, long maxBytes, CancellationToken ct)
    {
        using var response = await SendAsync(url, ct);
        var total = response.Content.Headers.ContentLength;
        if (total > maxBytes) throw new InvalidOperationException("The download is larger than expected.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            done += read;
            if (done > maxBytes) throw new InvalidOperationException("The download is larger than expected.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            if (total is > 0) progress?.Invoke(Math.Min(1.0, (double)done / total.Value));
        }
        await destination.FlushAsync(ct);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (!_policy.IsRedirectTarget(current))
                throw new UpdateSecurityException("Redirect to a host that is not allowed: " + current.Host);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); }
            catch { response.Dispose(); throw; }
            return response;
        }
        throw new HttpRequestException("Too many redirects.");
    }
}

/// <summary>Starts the downloaded installer. Returns false when it deliberately did not (Debug dry run).</summary>
public interface IInstallerLauncher
{
    bool Launch(string installerPath);
}

public sealed class ProcessInstallerLauncher : IInstallerLauncher
{
    /// <summary>Normal launch, no flags: the installer shows its own Update page, closes a running Remindr and upgrades in place keeping data.</summary>
    public bool Launch(string installerPath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? "",
        });
        return true;
    }
}

public enum UpdateState
{
    Idle, Checking, UpToDate, Available, Downloading, Verifying,
    /// <summary>Downloaded and the SHA-256 matched: waiting for "Restart and update".</summary>
    ReadyToInstall,
    Launching,
    /// <summary>Debug dry run: the file was verified and the launch was skipped on purpose.</summary>
    WouldLaunch,
    CheckFailed, DownloadFailed, HashMismatch,
}

/// <summary>
/// The startup update check and the verified download. UI-free. Always called from one thread at a time (the UI thread), so state
/// changes are plain property sets; nothing here blocks that thread (network and hashing are awaited / moved off it).
/// Safety: https and an allow-list of GitHub hosts only; the installer must come from this repository's release; its SHA-256 must
/// equal the release's .sha256 asset; on any mismatch the file is deleted and nothing is run.
/// </summary>
public sealed partial class UpdateService : ObservableObject
{
    public const string EnabledKey = "update_check_enabled";
    public const string SkippedKey = "update_skipped_version";
    public const long MaxInstallerBytes = 400L * 1024 * 1024;

    private readonly IUpdateClient _client;
    private readonly SettingsRepo _settings;
    private readonly UpdatePolicy _policy;
    private readonly IInstallerLauncher _launcher;
    private readonly IClock _clock;
    private readonly SemVer _current;
    private readonly string _tempDir;
    private readonly Uri _feed;
    private readonly Action<Exception, string> _log;
    private bool _loggedFailure;

    public UpdateService(IUpdateClient client, SettingsRepo settings, UpdatePolicy policy, IInstallerLauncher launcher,
        IClock clock, string currentVersion, string tempDir, Uri? feedUrl = null, Action<Exception, string>? log = null)
    {
        _client = client;
        _settings = settings;
        _policy = policy;
        _launcher = launcher;
        _clock = clock;
        _tempDir = tempDir;
        _log = log ?? DefaultLog;
        _feed = feedUrl ?? new Uri($"https://api.github.com/repos/{RepoInfo.Owner}/{RepoInfo.Repo}/releases/latest");
        if (!SemVer.TryParse(currentVersion, out _current)) _current = new SemVer(0, 0, 0, "");
    }

    /// <summary>
    /// A failed check (offline, 404 while the repository has no release, rate limit...) is expected and happens at every launch in those
    /// conditions, so it is one line in error.log, not a stack trace. Download and launch failures keep the full details.
    /// </summary>
    private static void DefaultLog(Exception ex, string context)
    {
        if (context.StartsWith("Update check", StringComparison.Ordinal))
            ErrorLog.WriteLine($"{context}: {ex.GetType().Name}: {ex.Message}");
        else
            ErrorLog.Write(ex, context);
    }

    /// <summary>Folder the installer is downloaded to (%TEMP%\Remindr-update).</summary>
    public static string DefaultTempDir => Path.Combine(Path.GetTempPath(), "Remindr-update");

    [ObservableProperty] private UpdateState _state = UpdateState.Idle;
    [ObservableProperty] private ReleaseInfo? _release;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private DateTime? _lastChecked;
    [ObservableProperty] private bool _dismissed;
    [ObservableProperty] private string? _launchedPath;

    /// <summary>Raised after the verified installer was really started: the app should exit now.</summary>
    public event EventHandler? InstallerStarted;

    /// <summary>The banner is shown for an update (or its download / error) the person has not dismissed this session.</summary>
    public bool ShowBanner => !Dismissed
        && State is UpdateState.Available or UpdateState.Downloading or UpdateState.Verifying
            or UpdateState.ReadyToInstall or UpdateState.Launching or UpdateState.WouldLaunch or UpdateState.DownloadFailed or UpdateState.HashMismatch;

    partial void OnStateChanged(UpdateState value) => OnPropertyChanged(nameof(ShowBanner));
    partial void OnDismissedChanged(bool value) => OnPropertyChanged(nameof(ShowBanner));

    /// <summary>Settings: check when Remindr starts (default on).</summary>
    public bool CheckEnabled
    {
        get => _settings.Get(EnabledKey) != "0";
        set
        {
            if (CheckEnabled == value) return;
            _settings.Set(EnabledKey, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    public string? SkippedVersion => _settings.Get(SkippedKey);

    /// <summary>Start-up entry point: honours the setting, never throws, never blocks.</summary>
    public async Task RunStartupCheckAsync()
    {
        try
        {
            // A verified installer from an earlier session (already run, or never run) is not reused: remove it. Best effort,
            // the installer may still be running from it right after an upgrade.
            DeleteQuietly(Path.Combine(_tempDir, ReleaseParser.InstallerAssetName));
            if (!CheckEnabled) return;
            await CheckAsync(manual: false);
        }
        catch (Exception ex)
        {
            _log(ex, "Update check");
        }
    }

    /// <summary>
    /// Asks GitHub for the latest release. A skipped version stays hidden on the automatic check; the manual check
    /// (Settings > About) shows it anyway. Offline, errors and rate limits end quietly in <see cref="UpdateState.CheckFailed"/>.
    /// </summary>
    public async Task CheckAsync(bool manual, CancellationToken ct = default)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Verifying or UpdateState.ReadyToInstall or UpdateState.Launching) return;
        var previous = State;
        State = UpdateState.Checking;
        try
        {
            if (!_policy.IsFeedUrl(_feed)) throw new UpdateSecurityException("Feed address is not allowed.");
            var json = await _client.GetStringAsync(_feed, ct);
            LastChecked = _clock.Now;
            var release = ReleaseParser.Parse(json, _policy);
            if (release is null || release.Version <= _current)
            {
                Release = null;
                State = UpdateState.UpToDate;
                return;
            }
            // A release that cannot be verified (no installer, no .sha256, or assets from elsewhere, e.g. still uploading) is
            // never offered: it ends quietly like any other failed check instead of showing a card whose Update can only fail.
            if (!IsVerifiable(release))
                throw new InvalidDataException($"Release {release.Tag} has no verifiable installer and checksum assets.");
            if (!manual && string.Equals(SkippedVersion, release.Version.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Release = release;
                State = UpdateState.UpToDate;
                return;
            }
            Release = release;
            Dismissed = false;
            State = UpdateState.Available;
        }
        catch (Exception ex)
        {
            // Offline, timeout, rate limit, bad answer: silent, logged once per run.
            State = previous is UpdateState.Available ? UpdateState.Available : UpdateState.CheckFailed;
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _log(ex, "Update check failed");
            }
        }
    }

    private bool IsVerifiable(ReleaseInfo r) =>
        Uri.TryCreate(r.InstallerUrl, UriKind.Absolute, out var installer) && _policy.IsRepoAssetUrl(installer)
        && Uri.TryCreate(r.ChecksumUrl, UriKind.Absolute, out var checksum) && _policy.IsRepoAssetUrl(checksum);

    /// <summary>"Later": hides the banner for this session only.</summary>
    public void Later() => Dismissed = true;

    /// <summary>"Skip this version": remembered across launches, until a newer version is released.</summary>
    public void Skip()
    {
        if (Release is { } r) _settings.Set(SkippedKey, r.Version.ToString());
        Dismissed = true;
    }

    private CancellationTokenSource? _downloadCts;
    private string? _verifiedHash;

    /// <summary>Stops a running download (the card's Cancel): the partial file is deleted and the update is offered again.</summary>
    public void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>
    /// Downloads the installer and checks its SHA-256 against the release's checksum asset. On success the state is
    /// <see cref="UpdateState.ReadyToInstall"/>; nothing is started until <see cref="InstallAsync"/>. Never runs an unverified file.
    /// </summary>
    public async Task UpdateNowAsync(CancellationToken ct = default)
    {
        if (Release is not { } release || State is UpdateState.Downloading or UpdateState.Verifying or UpdateState.Launching) return;
        Progress = 0;
        State = UpdateState.Downloading;
        var path = Path.Combine(_tempDir, ReleaseParser.InstallerAssetName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _downloadCts = cts;
        ct = cts.Token;
        try
        {
            if (!Uri.TryCreate(release.InstallerUrl, UriKind.Absolute, out var installer) || !_policy.IsRepoAssetUrl(installer))
                throw new UpdateSecurityException("The release has no installer from this repository.");
            if (!Uri.TryCreate(release.ChecksumUrl, UriKind.Absolute, out var checksum) || !_policy.IsRepoAssetUrl(checksum))
                throw new UpdateSecurityException("The release has no checksum file, so the download cannot be verified.");

            Directory.CreateDirectory(_tempDir);
            DeleteQuietly(path);

            var expected = ReleaseParser.ParseChecksum(await _client.GetStringAsync(checksum, ct))
                           ?? throw new InvalidDataException("The checksum file is not in the expected format.");

            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var last = -1.0;
                await _client.DownloadAsync(installer, file, p =>
                {
                    if (p - last < 0.01 && p < 1) return;
                    last = p;
                    Progress = p;
                }, MaxInstallerBytes, ct);
            }

            State = UpdateState.Verifying;
            var actual = await Task.Run(() => HashFile(path), ct);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                DeleteQuietly(path);
                State = UpdateState.HashMismatch;
                return;
            }

            LaunchedPath = path;
            _verifiedHash = actual;
            State = UpdateState.ReadyToInstall;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            DeleteQuietly(path);
            State = UpdateState.Available;
        }
        catch (Exception ex)
        {
            DeleteQuietly(path);
            State = UpdateState.DownloadFailed;
            _log(ex, "Update download");
        }
        finally
        {
            _downloadCts = null;
        }
    }

    /// <summary>"Restart and update": re-checks the verified file's hash once more, then starts the installer.</summary>
    public async Task InstallAsync()
    {
        if (State != UpdateState.ReadyToInstall || LaunchedPath is not { } path || _verifiedHash is null) return;
        State = UpdateState.Launching;
        try
        {
            var again = await Task.Run(() => HashFile(path));
            if (!string.Equals(again, _verifiedHash, StringComparison.OrdinalIgnoreCase))
            {
                DeleteQuietly(path);
                State = UpdateState.HashMismatch;
                return;
            }
            if (_launcher.Launch(path)) InstallerStarted?.Invoke(this, EventArgs.Empty);
            else State = UpdateState.WouldLaunch;
        }
        catch (Exception ex)
        {
            DeleteQuietly(path);
            State = UpdateState.DownloadFailed;
            _log(ex, "Update launch");
        }
    }

    public static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}

#if DEBUG
/// <summary>Debug only: REMINDR_UPDATE_FEED may be a local JSON file; everything else goes to the wrapped client.</summary>
public sealed class FileFeedClient : IUpdateClient
{
    private readonly IUpdateClient _inner;
    public FileFeedClient(IUpdateClient inner) => _inner = inner;

    public Task<string> GetStringAsync(Uri url, CancellationToken ct) =>
        url.IsFile ? File.ReadAllTextAsync(url.LocalPath, ct) : _inner.GetStringAsync(url, ct);

    public Task DownloadAsync(Uri url, Stream destination, Action<double>? progress, long maxBytes, CancellationToken ct) =>
        _inner.DownloadAsync(url, destination, progress, maxBytes, ct);
}

/// <summary>Debug only (REMINDR_UPDATE_NO_LAUNCH=1): stops after verification and records "would launch" instead of running the file.</summary>
public sealed class DryRunLauncher : IInstallerLauncher
{
    public bool Launch(string installerPath)
    {
        try { File.WriteAllText(Path.Combine(AppPaths.Current.DataDir, "update-would-launch.txt"), "would launch " + installerPath); }
        catch { /* the banner shows it too */ }
        return false;
    }
}
#endif
