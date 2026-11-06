using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace Remindr.Services.Assistant;

/// <summary>The model behind the assistant: a phrase in, the model's raw JSON call out. Tests use recorded answers instead of a model.</summary>
public interface IAssistantModel : IDisposable
{
    /// <summary>The graphics card the model runs on, or null for the CPU.</summary>
    string? Device { get; }

    /// <summary>A Loc key explaining why the model is not on the graphics card when the user asked for it (mode On), else null.</summary>
    string? NoteKey { get; }

    Task<string> InferAsync(string phrase, CancellationToken ct = default);

    /// <summary>Chat mode: a plain reply to the last turns, streamed token by token. No tools and no grammar.</summary>
    IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct = default);
}

/// <summary>One earlier message of a Chat conversation.</summary>
public sealed record ChatTurn(bool FromUser, string Text);

/// <summary>
/// The only class that touches LLamaSharp (llama.cpp in-process; Vulkan build with the CPU build as the fallback). The settings are the
/// spike's: context 2048, all layers on one dedicated card, <c>SplitMode.None</c> + <c>MainGpu = 0</c> so nothing lands on an integrated
/// GPU, no memory map (the weights are read straight into VRAM, not also kept mapped in system RAM), temperature 0, a stateless executor
/// and one inference at a time. <see cref="Load"/> blocks (the first Vulkan call compiles shaders for ~20-28 s): call it off the UI thread.
/// </summary>
public sealed class AssistantEngine : IAssistantModel
{
    static readonly GpuLog Log = new();
    static readonly object ConfigLock = new();
    static bool _configured;
    static bool _cpuOnly;

    readonly LLamaWeights _weights;
    readonly StatelessExecutor _executor;
    readonly StatelessExecutor _chatExecutor;
    readonly SemaphoreSlim _one = new(1, 1);
    bool _disposed;

    AssistantEngine(LLamaWeights weights, ModelParams parameters, ModelParams chatParameters, string? device, string? noteKey)
    {
        _weights = weights;
        _executor = new StatelessExecutor(weights, parameters);
        _chatExecutor = new StatelessExecutor(weights, chatParameters);
        Device = device;
        NoteKey = noteKey;
    }

    public string? Device { get; }
    public string? NoteKey { get; }

    /// <summary>
    /// LLamaSharp reads its backend choice only once, before the first native call, so a change between "graphics card" and "CPU only"
    /// needs a restart once the library is loaded (Auto and On share the Vulkan backend and switch live).
    /// </summary>
    public static bool NeedsRestart(GpuMode mode) { lock (ConfigLock) return _configured && (mode == GpuMode.Off) != _cpuOnly; }

    /// <summary>The device, "using device" and "offloaded" lines llama.cpp printed so far (for the self-check report).</summary>
    public static IReadOnlyList<string> CapturedGpuLines { get { lock (ConfigLock) return Log.Lines.ToList(); } }

    /// <summary>
    /// Loads the model. Off: 0 GPU layers on the CPU backend. Auto and On: with a dedicated card (llama.cpp lists it with <c>uma: 0</c>) all
    /// layers go there and the "using device" and "offloaded N/N" lines must prove it; any error, no card, or a partial offload loads again
    /// with 0 GPU layers (CPU). On also reports that through <see cref="NoteKey"/>. An integrated GPU is never used.
    /// </summary>
    public static AssistantEngine Load(string modelPath, GpuMode mode = GpuMode.Auto)
    {
        Configure(mode);
        // Touching the native library loads it, which makes llama.cpp print its device list through the log callback.
        _ = NativeApi.llama_supports_gpu_offload();
        var card = Log.DedicatedCard;
        if (GpuPolicy.TryGpu(mode, card))
        {
            try
            {
                var gpu = TryLoad(modelPath, 99, card);
                if (GpuPolicy.Outcome(mode, card, Log.FullyOn(card!)).Device is not null) return gpu;
                gpu._weights.Dispose();
            }
            catch (Exception ex) { ErrorLog.Write(ex, "Assistant graphics card"); }
        }
        return TryLoad(modelPath, 0, null, GpuPolicy.Outcome(mode, card, false).NoteKey);
    }

    static AssistantEngine TryLoad(string modelPath, int gpuLayers, string? device, string? noteKey = null)
    {
        Log.ResetLoad();
        var parameters = Params(modelPath, gpuLayers, 2048);
        var weights = LLamaWeights.LoadFromFile(parameters);
        return new AssistantEngine(weights, parameters, Params(modelPath, gpuLayers, 4096), device, noteKey);
    }

    static ModelParams Params(string modelPath, int gpuLayers, uint context) => new(modelPath)
    {
        ContextSize = context,
        GpuLayerCount = gpuLayers,
        Threads = Math.Max(1, Environment.ProcessorCount / 2),
        // Never pick a card by Vulkan number or set GGML_VK_VISIBLE_DEVICES: Vulkan reorders devices, and "0" once meant the iGPU.
        // With 0 GPU layers nothing goes to a card anyway, and the CPU-only backend has no device 0 for llama.cpp to accept as MainGpu.
        SplitMode = gpuLayers > 0 ? GPUSplitMode.None : GPUSplitMode.Layer,
        MainGpu = 0,
        UseMemorymap = false,
    };

    static void Configure(GpuMode mode)
    {
        lock (ConfigLock)
        {
            if (_configured) return;
            // Off: the CPU build only. Otherwise Vulkan (NVIDIA, AMD, Intel) with the CPU build as the fallback when it can't load.
            // Only the lines the device check reads are kept.
            _cpuOnly = mode == GpuMode.Off;
            NativeLibraryConfig.All.WithVulkan(!_cpuOnly).WithAutoFallback(true).WithLogCallback((_, msg) => Log.Feed(msg));
            _configured = true;
        }
    }

    public async Task<string> InferAsync(string phrase, CancellationToken ct = default)
    {
        await _one.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A caller that took this model before Unload must not touch freed native weights (a crash, not an exception).
            ObjectDisposedException.ThrowIf(_disposed, this);
            var template = new LLamaTemplate(_weights) { AddAssistant = true };
            template.Add("system", AssistantPrompt.SystemPrompt);
            foreach (var (u, c) in AssistantPrompt.Shots) { template.Add("user", u); template.Add("assistant", c); }
            template.Add("user", phrase);
            var prompt = Encoding.UTF8.GetString(template.Apply());
            var inference = new InferenceParams
            {
                MaxTokens = 256,
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0f, Grammar = new Grammar(AssistantPrompt.Gbnf, "root") },
            };
            var sb = new StringBuilder();
            await foreach (var t in _executor.InferAsync(prompt, inference, ct).ConfigureAwait(false)) sb.Append(t);
            return sb.ToString().Trim();
        }
        finally { _one.Release(); }
    }

    public async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _one.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var template = new LLamaTemplate(_weights) { AddAssistant = true };
            template.Add("system", AssistantPrompt.ChatSystemPrompt);
            foreach (var t in turns) template.Add(t.FromUser ? "user" : "assistant", t.Text);
            var prompt = Encoding.UTF8.GetString(template.Apply());
            var inference = new InferenceParams { MaxTokens = 160, SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f } };
            await foreach (var token in _chatExecutor.InferAsync(prompt, inference, ct).ConfigureAwait(false)) yield return token;
        }
        finally { _one.Release(); }
    }

    public void Dispose()
    {
        // Wait for a running answer to finish before the weights go away.
        _one.Wait();
        try { if (!_disposed) { _disposed = true; _weights.Dispose(); } }
        finally { _one.Release(); }
    }
}
