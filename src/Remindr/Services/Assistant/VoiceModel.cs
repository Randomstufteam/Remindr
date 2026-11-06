using System.IO;

namespace Remindr.Services.Assistant;

/// <summary>The one speech model: ggml-base.en from the official whisper.cpp Hugging Face repo (size and SHA-256 from its LFS entry). huggingface.co is already on the download allow-list.</summary>
public static class VoiceModelCatalog
{
    public const string Repo = "ggerganov/whisper.cpp";
    public static readonly ModelTier Whisper = new("whisper-base-en", "ggml-base.en.bin", 147_964_211, "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002", 0);
    public static Uri Url => new($"https://huggingface.co/{Repo}/resolve/main/{Whisper.File}");
}

/// <summary>
/// The natural reply voice: Kokoro-82M (hexgrad, Apache 2.0) in the ONNX export KokoroSharp reads, from the KokoroSharpBinaries v2.0.0 GitHub release
/// (sizes and SHA-256 from the release's asset digests). Two sizes of the same voice; the Hugging Face onnx-community export has different input names.
/// </summary>
public static class KokoroCatalog
{
    public const string Release = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/";
    public static readonly ModelTier Original = new("kokoro", "kokoro.onnx", 325_508_342, "0cfd5e79aab70a3d8c1a57dc639835110ddb32c9f5ff4fdd1f4db202ea43bb05", 0);
    public static readonly ModelTier Small = new("kokoro-fp16", "kokoro-fp16.onnx", 163_636_560, "027a25b14aef7d3ae57fd09301ebefbec868e79d55213d07e4f3af442f5ba352", 0);
    public static Uri UrlFor(ModelTier tier) => new(Release + tier.File);

    /// <summary>Stored voice names start with this; anything else is a Windows voice.</summary>
    public const string Prefix = "kokoro:";
    /// <summary>The English voices shipped in the app's voices folder (Remindr.csproj copies the same list). The first is the default.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Voices = new[]
    {
        ("af_heart", "Heart"), ("af_bella", "Bella"), ("am_michael", "Michael"), ("am_fenrir", "Fenrir"), ("bf_emma", "Emma (UK)"), ("bm_george", "George (UK)"),
    };

    /// <summary>That release on github.com, and GitHub's release-asset CDN it redirects to. https only; the pinned SHA-256 still checks the file.</summary>
    public static bool IsAllowedHost(Uri u)
    {
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return (h == "github.com" && u.AbsolutePath.StartsWith("/Lyrcaxis/KokoroSharpBinaries/releases/download/", StringComparison.Ordinal))
            || h is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    }
}

/// <summary>Downloads, pauses, resumes, cancels and removes one voice model file (Whisper by default, or a Kokoro size) with the same <see cref="ModelDownloader"/> as the text model. UI-free; <see cref="Changed"/> may fire on any thread.</summary>
public sealed class VoiceModelManager
{
    private readonly string _dir;
    private readonly ModelDownloader _downloader;
    private readonly Uri _url;
    private CancellationTokenSource? _cts;
    private bool _deleteOnStop;

    public VoiceModelManager(AppPaths paths) : this(paths.ModelsDir, new ModelDownloader(ModelDownloader.CreateHandler())) { }

    public VoiceModelManager(string modelsDir, ModelDownloader downloader, ModelTier? tier = null, Uri? url = null)
    {
        _dir = modelsDir;
        _downloader = downloader;
        Tier = tier ?? VoiceModelCatalog.Whisper;
        _url = url ?? VoiceModelCatalog.Url;
        if (!Installed && File.Exists(PartPath))
        {
            BytesDone = new FileInfo(PartPath).Length;
            State = ModelDownloadState.Paused;
        }
    }

    public event EventHandler? Changed;
    /// <summary>The model file appeared or was removed.</summary>
    public event EventHandler? InstalledChanged;

    public ModelTier Tier { get; }
    private string PartPath => ModelDownloader.PartPath(_dir, Tier);
    private string FilePath => ModelCatalog.PathOf(Tier, _dir);
    public string? InstalledPath => Installed ? FilePath : null;
    public bool Installed => File.Exists(FilePath);
    public ModelDownloadState State { get; private set; }
    public long BytesDone { get; private set; }
    public double Progress => Math.Min(1.0, (double)BytesDone / Tier.Bytes);
    public string? ErrorKey { get; private set; }
    public bool IsBusy => State is ModelDownloadState.Downloading or ModelDownloadState.Verifying;

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public async Task StartAsync()
    {
        if (IsBusy || Installed) return;
        var tier = Tier;
        ErrorKey = null;
        BytesDone = File.Exists(PartPath) ? new FileInfo(PartPath).Length : 0;
        State = ModelDownloadState.Downloading;
        _deleteOnStop = false;
        using var cts = _cts = new CancellationTokenSource();
        Raise();
        var last = 0L;
        try
        {
            var result = await _downloader.DownloadAsync(tier, _url, _dir, done =>
            {
                BytesDone = done;
                if (done - last < (1 << 20) && done < tier.Bytes) return;
                last = done;
                Raise();
            }, () => { State = ModelDownloadState.Verifying; Raise(); }, cts.Token);
            if (result == ModelDownloadResult.HashMismatch) { Fail(ModelDownloadState.Failed, "Assistant_Dl_HashMismatch"); return; }
            State = ModelDownloadState.Idle;
            InstalledChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (_deleteOnStop) { Delete(PartPath); State = ModelDownloadState.Idle; BytesDone = 0; }
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

    public void Cancel()
    {
        if (_cts is not null) { _deleteOnStop = true; _cts.Cancel(); return; }
        Delete(PartPath);
        State = ModelDownloadState.Idle;
        BytesDone = 0;
        ErrorKey = null;
        Raise();
    }

    public void Remove()
    {
        if (IsBusy) return;
        Delete(FilePath);
        Delete(PartPath);
        State = ModelDownloadState.Idle;
        BytesDone = 0;
        ErrorKey = null;
        InstalledChanged?.Invoke(this, EventArgs.Empty);
        Raise();
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}

/// <summary>The two Kokoro sizes. The original wins when both are downloaded; removing one frees the loaded voice so the other (or Windows) takes over.</summary>
public sealed class NaturalVoiceModels
{
    public NaturalVoiceModels(AppPaths paths) : this(paths.ModelsDir, new ModelDownloader(ModelDownloader.CreateHandler(), KokoroCatalog.IsAllowedHost)) { }

    public NaturalVoiceModels(string modelsDir, ModelDownloader downloader)
    {
        Original = new VoiceModelManager(modelsDir, downloader, KokoroCatalog.Original, KokoroCatalog.UrlFor(KokoroCatalog.Original));
        Small = new VoiceModelManager(modelsDir, downloader, KokoroCatalog.Small, KokoroCatalog.UrlFor(KokoroCatalog.Small));
        Original.InstalledChanged += OnInstalledChanged;
        Small.InstalledChanged += OnInstalledChanged;
    }

    public VoiceModelManager Original { get; }
    public VoiceModelManager Small { get; }
    /// <summary>Set once at startup so a removed model is not kept in memory.</summary>
    public NaturalSpeaker? Speaker { get; set; }
    public string? InstalledPath => Original.InstalledPath ?? Small.InstalledPath;
    public event EventHandler? InstalledChanged;

    private void OnInstalledChanged(object? sender, EventArgs e)
    {
        Speaker?.Unload();
        InstalledChanged?.Invoke(this, EventArgs.Empty);
    }
}
