using System.Text.RegularExpressions;

namespace Remindr.Services.Assistant;

/// <summary>
/// What llama.cpp's log says about graphics cards: the Vulkan device list (with its <c>uma</c> flag: 1 = integrated, shares system RAM),
/// the "using device" line and "offloaded N/N layers". Cards are never chosen by Vulkan number (Vulkan reorders them when the NVIDIA card
/// sleeps) and GGML_VK_VISIBLE_DEVICES is never set: llama.cpp leaves the integrated GPU out when a dedicated one exists, and the
/// check after loading proves the model is fully on that card. UI-free (fed one log line at a time).
/// </summary>
public sealed class GpuLog
{
    /// <summary>Every device, "using device" and "offloaded" line seen, as llama.cpp printed it (for the self-check report).</summary>
    public List<string> Lines { get; } = new();

    static readonly Regex Device = new(@"ggml_vulkan: (\d+) = (.+?)(?: \([^)]*\))? \|.*uma: (\d)", RegexOptions.Compiled);
    static readonly Regex Offload = new(@"offloaded (\d+)/(\d+) layers", RegexOptions.Compiled);

    readonly List<(string Name, bool SharesRam)> _gpus = new();
    readonly object _lock = new();

    public string? UsedDevice { get; private set; }
    public (int Done, int Of) Offloaded { get; private set; }

    public void Feed(string line)
    {
        lock (_lock)
        {
            if (Device.IsMatch(line) || line.Contains("using device") || Offload.IsMatch(line) || line.Contains("error", StringComparison.OrdinalIgnoreCase)) Lines.Add(line.TrimEnd());
            if (Device.Match(line) is { Success: true } d) _gpus.Add((d.Groups[2].Value, d.Groups[3].Value == "1"));
            if (line.Contains("using device")) UsedDevice = line;
            if (Offload.Match(line) is { Success: true } o) Offloaded = (int.Parse(o.Groups[1].Value), int.Parse(o.Groups[2].Value));
        }
    }

    /// <summary>Forget the model-load lines (the device list stays: it is printed once per process).</summary>
    public void ResetLoad() { lock (_lock) { UsedDevice = null; Offloaded = (0, 0); } }

    /// <summary>The first dedicated card (its own VRAM), or null when there are none or only integrated ones.</summary>
    public string? DedicatedCard { get { lock (_lock) return _gpus.Where(g => !g.SharesRam).Select(g => g.Name).FirstOrDefault(); } }

    /// <summary>True when every layer is on <paramref name="card"/> and llama.cpp's "using device" line names it.</summary>
    public bool FullyOn(string card) { lock (_lock) return UsedDevice is not null && UsedDevice.Contains(card) && Offloaded.Of > 0 && Offloaded.Done == Offloaded.Of; }
}

/// <summary>The "Use graphics card" setting (<c>assistant_gpu</c>, global): Auto, On or Off.</summary>
public enum GpuMode { Auto, On, Off }

/// <summary>What each <see cref="GpuMode"/> does with what llama.cpp reported. UI-free.</summary>
public static class GpuPolicy
{
    public const string Key = "assistant_gpu";
    /// <summary>The Loc key of the "On" mode's message when it could not use a dedicated card.</summary>
    public const string NoCardNote = "Assistant_Gpu_NoCard";

    public static GpuMode Parse(string? value) => value switch { "on" => GpuMode.On, "off" => GpuMode.Off, _ => GpuMode.Auto };
    public static string Format(GpuMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>Whether to try the graphics card at all: never when Off, and never an integrated one (<paramref name="card"/> is null then).</summary>
    public static bool TryGpu(GpuMode mode, string? card) => mode != GpuMode.Off && card is not null;

    /// <summary>
    /// The device the model ended up on (null = CPU) and the note to show. <paramref name="fullyOn"/> is llama.cpp's proof that every layer is
    /// on <paramref name="card"/>. Auto falls back to the CPU quietly; On says so.
    /// </summary>
    public static (string? Device, string? NoteKey) Outcome(GpuMode mode, string? card, bool fullyOn)
    {
        if (mode == GpuMode.Off) return (null, null);
        if (card is not null && fullyOn) return (card, null);
        return (null, mode == GpuMode.On ? NoCardNote : null);
    }
}
