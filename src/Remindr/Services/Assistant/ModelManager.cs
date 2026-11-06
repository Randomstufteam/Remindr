using System.IO;
using Remindr.Data.Repositories;

namespace Remindr.Services.Assistant;

public enum ModelDownloadState { Idle, Downloading, Paused, Verifying, Failed }

/// <summary>
/// The model files on disk and the one download in progress: which tier is installed, start / pause / resume / cancel, switch tier (the new
/// file is downloaded and verified first, then the old one is deleted) and Remove. A .part file left by an earlier run shows as Paused so
/// Resume continues from where it stopped. UI-free; <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class ModelManager
{
    private readonly string _dir;
    private readonly SettingsRepo _settings;
    private readonly ModelDownloader _downloader;
    private readonly Func<long> _totalRam;
    private CancellationTokenSource? _cts;
    private bool _deleteOnStop;

    public ModelManager(AppPaths paths, SettingsRepo settings) : this(paths.ModelsDir, settings, new ModelDownloader(ModelDownloader.CreateHandler()), ModelCatalog.TotalRam) { }

    public ModelManager(string modelsDir, SettingsRepo settings, ModelDownloader downloader, Func<long> totalRam)
    {
        _dir = modelsDir;
        _settings = settings;
        _downloader = downloader;
        _totalRam = totalRam;
        if (Installed is null && ModelCatalog.Tiers.FirstOrDefault(t => File.Exists(ModelDownloader.PartPath(_dir, t))) is { } left)
        {
            Target = left;
            BytesDone = new FileInfo(ModelDownloader.PartPath(_dir, left)).Length;
            State = ModelDownloadState.Paused;
        }
    }

    /// <summary>Raised when the state or progress changed.</summary>
    public event EventHandler? Changed;
    /// <summary>Raised when the installed file changed (a download finished, or Remove): the engine must reload or unload.</summary>
    public event EventHandler? InstalledChanged;

    public string ModelsDir => _dir;
    public long TotalRam => _totalRam();
    public ModelDownloadState State { get; private set; }
    /// <summary>The tier being downloaded (or paused).</summary>
    public ModelTier? Target { get; private set; }
    public long BytesDone { get; private set; }
    public double Progress => Target is { } t ? Math.Min(1.0, (double)BytesDone / t.Bytes) : 0;
    /// <summary>A Loc key describing the last problem (paused by a lost connection, hash mismatch, no disk space), or null.</summary>
    public string? ErrorKey { get; private set; }

    /// <summary>The tier whose complete file is in the models folder (the saved choice if several), or null.</summary>
    public ModelTier? Installed
    {
        get
        {
            var found = ModelCatalog.Tiers.Where(t => File.Exists(ModelCatalog.PathOf(t, _dir))).ToList();
            return found.FirstOrDefault(t => t.Id == _settings.Get(ModelCatalog.TierKey)) ?? found.FirstOrDefault();
        }
    }

    public string? InstalledPath => Installed is { } t ? ModelCatalog.PathOf(t, _dir) : null;

    /// <summary>The picker's choice: the installed tier, else the saved one, else Q4 (recommended). A tier this PC cannot run is never chosen.</summary>
    public ModelTier Selected
    {
        get
        {
            var tier = Installed ?? ModelCatalog.Find(_settings.Get(ModelCatalog.TierKey)) ?? ModelCatalog.Default;
            return ModelCatalog.CanRun(tier, TotalRam) ? tier : ModelCatalog.Default;
        }
        set { _settings.Set(ModelCatalog.TierKey, value.Id); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public bool IsBusy => State is ModelDownloadState.Downloading or ModelDownloadState.Verifying;

    public async Task StartAsync(ModelTier tier)
    {
        if (IsBusy || !ModelCatalog.CanRun(tier, TotalRam)) return;
        Target = tier;
        ErrorKey = null;
        BytesDone = File.Exists(ModelDownloader.PartPath(_dir, tier)) ? new FileInfo(ModelDownloader.PartPath(_dir, tier)).Length : 0;
        State = ModelDownloadState.Downloading;
        _deleteOnStop = false;
        using var cts = _cts = new CancellationTokenSource();
        Changed?.Invoke(this, EventArgs.Empty);
        var last = 0L;
        try
        {
            var result = await _downloader.DownloadAsync(tier, ModelCatalog.UrlFor(tier), _dir, done =>
            {
                BytesDone = done;
                if (done - last < (1 << 20) && done < tier.Bytes) return;   // about once per MB
                last = done;
                Changed?.Invoke(this, EventArgs.Empty);
            }, () => { State = ModelDownloadState.Verifying; Changed?.Invoke(this, EventArgs.Empty); }, cts.Token);

            if (result == ModelDownloadResult.HashMismatch)
            {
                Fail(ModelDownloadState.Failed, "Assistant_Dl_HashMismatch");
                return;
            }
            _settings.Set(ModelCatalog.TierKey, tier.Id);
            State = ModelDownloadState.Idle;
            Target = null;
            InstalledChanged?.Invoke(this, EventArgs.Empty);
            // Switching tier: the engine has been told, now the old file can go.
            foreach (var other in ModelCatalog.Tiers.Where(t => t != tier)) Delete(ModelCatalog.PathOf(other, _dir));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (_deleteOnStop) { Delete(ModelDownloader.PartPath(_dir, tier)); State = ModelDownloadState.Idle; Target = null; BytesDone = 0; }
            else State = ModelDownloadState.Paused;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (NotEnoughSpaceException) { Fail(ModelDownloadState.Failed, "Assistant_Dl_NoSpace"); }
        catch (Exception ex)
        {
            // A lost connection keeps the .part file: Resume continues.
            ErrorLog.Write(ex, "Model download");
            Fail(ModelDownloadState.Paused, "Assistant_Dl_ConnectionLost");
        }
        finally { _cts = null; }
    }

    void Fail(ModelDownloadState state, string key)
    {
        State = state;
        ErrorKey = key;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops the download and keeps the .part file (Resume continues from there).</summary>
    public void Pause() { _deleteOnStop = false; _cts?.Cancel(); }

    /// <summary>Stops the download and deletes the .part file (also from a paused state).</summary>
    public void Cancel()
    {
        if (_cts is not null) { _deleteOnStop = true; _cts.Cancel(); return; }
        if (Target is { } t) Delete(ModelDownloader.PartPath(_dir, t));
        State = ModelDownloadState.Idle;
        Target = null;
        BytesDone = 0;
        ErrorKey = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes every downloaded model file (turning the assistant off keeps them; this is the Remove button).</summary>
    public void Remove()
    {
        if (IsBusy) return;
        foreach (var t in ModelCatalog.Tiers) { Delete(ModelCatalog.PathOf(t, _dir)); Delete(ModelDownloader.PartPath(_dir, t)); }
        State = ModelDownloadState.Idle;
        Target = null;
        BytesDone = 0;
        ErrorKey = null;
        InstalledChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
