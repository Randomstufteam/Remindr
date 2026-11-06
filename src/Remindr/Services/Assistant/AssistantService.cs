using System.IO;
using System.Runtime.CompilerServices;
using Remindr.Data.Repositories;

namespace Remindr.Services.Assistant;

public enum AssistantState
{
    /// <summary>Turned off in Settings: no model in memory.</summary>
    Off,
    /// <summary>On, but the model file isn't there.</summary>
    NotInstalled,
    /// <summary>Loading the model (the first Vulkan call takes ~20-28 s).</summary>
    Warming,
    Ready,
    /// <summary>The model file is there but couldn't be loaded.</summary>
    Failed,
}

/// <summary>
/// Owns the assistant's on/off setting (<c>assistant_enabled</c>, global, off by default) and the model's life: loaded lazily, off the UI
/// thread, on first use; disposed when the assistant is turned off, so the RAM comes back. UI-free. The model is created through
/// <see cref="_load"/>, so tests can use a fake instead of LLamaSharp.
/// </summary>
public sealed class AssistantService
{
    public const string EnabledKey = "assistant_enabled";

    private readonly SettingsRepo _settings;
    private readonly Func<string, GpuMode, IAssistantModel> _load;
    private readonly Func<string?> _modelPath;
    private readonly object _lock = new();
    private IAssistantModel? _model;
    private Task? _loading;
    private bool _failed;
    // Bumped by Unload: a load that started before (old file, old GPU mode, assistant turned off) is dropped when it finishes.
    private int _generation;

    public AssistantService(SettingsRepo settings, ModelManager models)
        : this(settings, (path, mode) => AssistantEngine.Load(path, mode), () => models.InstalledPath)
    {
        // A finished download, a tier switch or Remove: drop the old weights and load the new file (if the assistant is on).
        models.InstalledChanged += (_, _) =>
        {
            Unload();
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (Enabled && models.InstalledPath is not null) _ = EnsureReadyAsync();
        };
    }

    public AssistantService(SettingsRepo settings, Func<string, IAssistantModel> load, string modelPath)
        : this(settings, (path, _) => load(path), () => modelPath) { }

    public AssistantService(SettingsRepo settings, Func<string, GpuMode, IAssistantModel> load, Func<string?> modelPath)
    {
        _settings = settings;
        _load = load;
        _modelPath = modelPath;
    }

    /// <summary>"Use graphics card" (global, default Auto).</summary>
    public GpuMode Gpu
    {
        get => GpuPolicy.Parse(_settings.Get(GpuPolicy.Key));
        set
        {
            if (Gpu == value) return;
            _settings.Set(GpuPolicy.Key, GpuPolicy.Format(value));
            // Auto and On share the Vulkan backend, so they switch live by reloading. A change to or from Off cannot (see NeedsRestart).
            if (!GpuNeedsRestart) { Unload(); if (Enabled) _ = EnsureReadyAsync(); }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The chosen mode only takes effect after restarting Remindr (the native backend is chosen once per process).</summary>
    public bool GpuNeedsRestart => AssistantEngine.NeedsRestart(Gpu);

    /// <summary>The Loc key of the "no dedicated graphics card" message (mode On), or null.</summary>
    public string? GpuNoteKey => _model?.NoteKey;

    /// <summary>Raised (on any thread) when <see cref="State"/> may have changed.</summary>
    public event EventHandler? StateChanged;

    public bool Enabled
    {
        get => _settings.Get(EnabledKey) == "1";
        set
        {
            if (Enabled == value) return;
            _settings.Set(EnabledKey, value ? "1" : "0");
            if (!value) Unload();
            EnabledChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? EnabledChanged;

    /// <summary>The graphics card in use, or null for the CPU (and while nothing is loaded).</summary>
    public string? Device => _model?.Device;

    /// <summary>True while a model is loaded (so <see cref="Device"/> null really means the CPU).</summary>
    public bool IsLoaded => _model is not null;

    /// <summary>Why the last load failed (for the page and the error log).</summary>
    public string? FailureMessage { get; private set; }

    public AssistantState State
    {
        get
        {
            if (!Enabled) return AssistantState.Off;
            lock (_lock)
            {
                if (_model is not null) return AssistantState.Ready;
                if (_loading is { IsCompleted: false }) return AssistantState.Warming;
                if (_failed) return AssistantState.Failed;
            }
            return _modelPath() is { } p && File.Exists(p) ? AssistantState.Warming : AssistantState.NotInstalled;
        }
    }

    /// <summary>Starts loading the model in the background if it isn't loaded yet. Returns the final state. Never throws.</summary>
    public async Task<AssistantState> EnsureReadyAsync()
    {
        Task? loading;
        lock (_lock)
        {
            if (!Enabled || _model is not null) return State;
            if (_modelPath() is not { } path || !File.Exists(path)) { _failed = false; return AssistantState.NotInstalled; }
            var generation = _generation;
            _loading ??= Task.Run(() => LoadModel(path, generation));
            loading = _loading;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        await loading.ConfigureAwait(false);
        return State;
    }

    private void LoadModel(string path, int generation)
    {
        var stale = false;
        try
        {
            var model = _load(path, Gpu);
            lock (_lock)
            {
                // Turned off, or the file / GPU mode changed while it was loading: drop it (and load again below if still on).
                stale = !Enabled || generation != _generation;
                if (!stale) { _model = model; _failed = false; }
            }
            if (stale) model.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Assistant model");
            lock (_lock)
            {
                stale = generation != _generation;
                if (!stale) { FailureMessage = ex.GetBaseException().Message; _failed = true; }
            }
        }
        finally
        {
            lock (_lock) _loading = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        if (stale && Enabled) _ = EnsureReadyAsync();
    }

    /// <summary>The guarded call for a phrase (loads the model first if needed). Null when the assistant isn't ready.</summary>
    public async Task<string?> AskAsync(string phrase, CancellationToken ct = default)
    {
        if (await EnsureReadyAsync().ConfigureAwait(false) != AssistantState.Ready) return null;
        IAssistantModel? model;
        lock (_lock) model = _model;
        if (model is null) return null;
        var (_, final) = await AssistantResolver.ResolveAsync(phrase, p => model.InferAsync(p, ct)).ConfigureAwait(false);
        return final;
    }

    /// <summary>Chat mode: a plain reply streamed token by token (loads the model first if needed). Empty when the assistant isn't ready. Never touches reminders.</summary>
    public async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (await EnsureReadyAsync().ConfigureAwait(false) != AssistantState.Ready) yield break;
        IAssistantModel? model;
        lock (_lock) model = _model;
        if (model is null) yield break;
        await foreach (var token in model.ChatAsync(turns, ct).ConfigureAwait(false)) yield return token;
    }

    /// <summary>Frees the model (turning the assistant off, or a profile deleted its data). A load in progress is dropped when it finishes.</summary>
    public void Unload()
    {
        IAssistantModel? model;
        lock (_lock) { model = _model; _model = null; _failed = false; _generation++; }
        // Off the UI thread: Dispose waits for an answer still running, which can take seconds on the CPU.
        if (model is not null) _ = Task.Run(model.Dispose);
    }
}
