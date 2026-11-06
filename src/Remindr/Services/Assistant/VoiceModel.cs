using System.IO;

namespace Remindr.Services.Assistant;

/// <summary>
/// The voice: LFM3-Audio-2B (Liquid AI, LFM Open License v1.0) hears you and talks back, run by llama.cpp's own <c>llama-server</c> (b11960, the
/// first build that supports it; MIT). Three pinned files: the Q4_K_M model and its Q8_0 audio projector from the Hugging Face repo at a fixed
/// commit (size and SHA-256 from the LFS entries), and the Windows x64 Vulkan build of llama.cpp from its GitHub release (the asset digest).
/// </summary>
public static class AudioModelCatalog
{
    public const string Repo = "LiquidAI/LFM3-Audio-2B-GGUF";
    const string Commit = "39670cb5c42e9c4bfe046519ba7ffbe47f9552ad";
    public const string RuntimeBuild = "b11960";
    const string RuntimeRelease = "https://github.com/ggml-org/llama.cpp/releases/download/" + RuntimeBuild + "/";

    public static readonly ModelTier Model = new("lfm3-audio", "LFM3-Audio-2B-Q4_K_M.gguf", 1_099_626_016, "9a773289bd321ed879cd3a76545f6aca506e403851a45fb315420aee45625be2", 0);
    public static readonly ModelTier Projector = new("lfm3-audio-mmproj", "mmproj-LFM3-Audio-2B-Q8_0.gguf", 303_811_840, "2b6648b1fd48af16aa0877b0592b144d15070783673e038a379fdbc51d40e365", 0);
    public static readonly ModelTier Runtime = new("llama-runtime", "llama-" + RuntimeBuild + "-bin-win-vulkan-x64.zip", 37_345_851, "bac6914b5e83523eb008c6789eaa12669f93fbc196c3b9e8237b6672323b33e9", 0);

    /// <summary>Every file the voice needs, in download order.</summary>
    public static IReadOnlyList<(ModelTier Tier, Uri Url)> Parts { get; } = new[]
    {
        (Model, new Uri($"https://huggingface.co/{Repo}/resolve/{Commit}/{Model.File}")),
        (Projector, new Uri($"https://huggingface.co/{Repo}/resolve/{Commit}/{Projector.File}")),
        (Runtime, new Uri(RuntimeRelease + Runtime.File)),
    };

    /// <summary>Where the runtime zip is unpacked.</summary>
    public static string RuntimeDir(string modelsDir) => Path.Combine(modelsDir, "llama-" + RuntimeBuild);

    /// <summary>Hugging Face (as for the text model), that llama.cpp release on github.com and GitHub's release-asset CDN. https only; the pinned SHA-256 still checks each file.</summary>
    public static bool IsAllowedHost(Uri u)
    {
        if (ModelCatalog.IsAllowedHost(u)) return true;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return (h == "github.com" && u.AbsolutePath.StartsWith("/ggml-org/llama.cpp/releases/download/", StringComparison.Ordinal))
            || h is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    }

    /// <summary>Stored voice names start with this; anything else is a Windows voice.</summary>
    public const string Prefix = "lfm:";
    /// <summary>The voices LFM3-Audio offers. The first is the default.</summary>
    public static readonly IReadOnlyList<(string Id, string NameKey)> Voices = new[] { ("female", "Voice_Lfm_Female"), ("male", "Voice_Lfm_Male") };
}

/// <summary>
/// Downloads, pauses, resumes, cancels and removes the voice's files (one after another, as one download) with the same <see cref="ModelDownloader"/>
/// as the text model; every file is checked against its pinned SHA-256 before it is renamed into place. UI-free; <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class VoiceModelManager
{
    private readonly string _dir;
    private readonly ModelDownloader _downloader;
    private readonly IReadOnlyList<(ModelTier Tier, Uri Url)> _parts;
    private CancellationTokenSource? _cts;
    private bool _deleteOnStop;

    public VoiceModelManager(AppPaths paths) : this(paths.ModelsDir, new ModelDownloader(ModelDownloader.CreateHandler(), AudioModelCatalog.IsAllowedHost)) { }

    public VoiceModelManager(string modelsDir, ModelDownloader downloader, IReadOnlyList<(ModelTier Tier, Uri Url)>? parts = null)
    {
        _dir = modelsDir;
        _downloader = downloader;
        _parts = parts ?? AudioModelCatalog.Parts;
        TotalBytes = _parts.Sum(p => p.Tier.Bytes);
        if (!Installed && _parts.Any(p => File.Exists(PartPath(p.Tier))))
        {
            BytesDone = DoneOnDisk();
            State = ModelDownloadState.Paused;
        }
    }

    public event EventHandler? Changed;
    /// <summary>The files appeared or were removed.</summary>
    public event EventHandler? InstalledChanged;
    /// <summary>Raised before Remove deletes the files, so whatever has them open can let go first (Windows can't delete open files).</summary>
    public event EventHandler? Removing;

    public string ModelsDir => _dir;
    public long TotalBytes { get; }
    private string PartPath(ModelTier t) => ModelDownloader.PartPath(_dir, t);
    private string FilePath(ModelTier t) => ModelCatalog.PathOf(t, _dir);
    public bool Installed => _parts.All(p => File.Exists(FilePath(p.Tier)));
    public ModelDownloadState State { get; private set; }
    public long BytesDone { get; private set; }
    public double Progress => Math.Min(1.0, (double)BytesDone / TotalBytes);
    public string? ErrorKey { get; private set; }
    public bool IsBusy => State is ModelDownloadState.Downloading or ModelDownloadState.Verifying;

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Bytes already on disk: finished files plus any .part.</summary>
    private long DoneOnDisk() => _parts.Sum(p => File.Exists(FilePath(p.Tier)) ? p.Tier.Bytes : File.Exists(PartPath(p.Tier)) ? new FileInfo(PartPath(p.Tier)).Length : 0);

    public async Task StartAsync()
    {
        if (IsBusy || Installed) return;
        ErrorKey = null;
        BytesDone = DoneOnDisk();
        State = ModelDownloadState.Downloading;
        _deleteOnStop = false;
        using var cts = _cts = new CancellationTokenSource();
        Raise();
        try
        {
            foreach (var (tier, url) in _parts)
            {
                if (File.Exists(FilePath(tier))) continue;
                State = ModelDownloadState.Downloading;
                var before = DoneOnDisk() - (File.Exists(PartPath(tier)) ? new FileInfo(PartPath(tier)).Length : 0);
                var last = 0L;
                var result = await _downloader.DownloadAsync(tier, url, _dir, done =>
                {
                    BytesDone = before + done;
                    if (done - last < (1 << 20) && done < tier.Bytes) return;
                    last = done;
                    Raise();
                }, () => { State = ModelDownloadState.Verifying; Raise(); }, cts.Token);
                if (result == ModelDownloadResult.HashMismatch) { BytesDone = DoneOnDisk(); Fail(ModelDownloadState.Failed, "Assistant_Dl_HashMismatch"); return; }
            }
            State = ModelDownloadState.Idle;
            InstalledChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (_deleteOnStop) { DeleteAll(); State = ModelDownloadState.Idle; BytesDone = 0; }
            else State = ModelDownloadState.Paused;
            Raise();
        }
        catch (NotEnoughSpaceException) { Fail(ModelDownloadState.Failed, "Assistant_Dl_NoSpace"); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Voice model download");
            Fail(ModelDownloadState.Paused, "Assistant_Dl_ConnectionLost");
        }
        finally { _cts = null; }
    }

    private void Fail(ModelDownloadState state, string key) { State = state; ErrorKey = key; Raise(); }

    public void Pause() { _deleteOnStop = false; _cts?.Cancel(); }

    /// <summary>Stops the download and deletes what it fetched so far, finished files included (they are useless on their own).</summary>
    public void Cancel()
    {
        if (_cts is not null) { _deleteOnStop = true; _cts.Cancel(); return; }
        DeleteAll();
        State = ModelDownloadState.Idle;
        BytesDone = 0;
        ErrorKey = null;
        Raise();
    }

    public void Remove()
    {
        if (IsBusy) return;
        Removing?.Invoke(this, EventArgs.Empty);
        DeleteAll();
        try { if (Directory.Exists(AudioModelCatalog.RuntimeDir(_dir))) Directory.Delete(AudioModelCatalog.RuntimeDir(_dir), true); } catch { /* best effort */ }
        State = ModelDownloadState.Idle;
        BytesDone = 0;
        ErrorKey = null;
        InstalledChanged?.Invoke(this, EventArgs.Empty);
        Raise();
    }

    private void DeleteAll() { foreach (var (tier, _) in _parts) { Delete(FilePath(tier)); Delete(PartPath(tier)); } }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
