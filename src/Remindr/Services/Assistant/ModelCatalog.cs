using System.IO;
using System.Runtime.InteropServices;

namespace Remindr.Services.Assistant;

/// <summary>One downloadable model file: exact size and SHA-256 are pinned here (Hugging Face's <c>lfs.oid</c> is the SHA-256).</summary>
public sealed record ModelTier(string Id, string File, long Bytes, string Sha256, long MinRamBytes);

/// <summary>
/// The three quantisations of LiquidAI/LFM2.5-1.2B-Instruct-GGUF the picker offers: Q4_K_M (recommended), Q8_0 and BF16. A tier is greyed out
/// when this PC has less total RAM than <see cref="ModelTier.MinRamBytes"/>. UI-free.
/// </summary>
public static class ModelCatalog
{
    const long GB = 1L << 30;
    public const string TierKey = "assistant_model_tier";
    public const string Repo = "LiquidAI/LFM2.5-1.2B-Instruct-GGUF";
    public const string LicenseFile = "LICENSE-LFM-Open-License-v1.0.txt";

    public static readonly IReadOnlyList<ModelTier> Tiers = new[]
    {
        new ModelTier("q4", "LFM2.5-1.2B-Instruct-Q4_K_M.gguf", 730_895_168, "b1b3de114215d9507409a662a501a631095a479a419584e8a2ded6304b19b4f5", 4 * GB),
        new ModelTier("q8", "LFM2.5-1.2B-Instruct-Q8_0.gguf", 1_246_253_888, "f6b981dcb86917fa463f78a362320bd5e2dc45445df147287eedb85e5a30d26a", 6 * GB),
        new ModelTier("bf16", "LFM2.5-1.2B-Instruct-BF16.gguf", 2_343_326_528, "3d80914b903cd6f3cc041208cf20ec46a3224f840c732e5fd7698832b4743d1b", 8 * GB),
    };

    /// <summary>Pre-selected: Q4 ("Recommended").</summary>
    public static ModelTier Default => Tiers[0];

    public static ModelTier? Find(string? id) => Tiers.FirstOrDefault(t => t.Id == id);

    public static bool CanRun(ModelTier tier, long totalRamBytes) => totalRamBytes >= tier.MinRamBytes;

    public static string PathOf(ModelTier tier, string modelsDir) => Path.Combine(modelsDir, tier.File);

    /// <summary>Where the app downloads from. Debug builds can point it at a local server with REMINDR_MODEL_BASE_URL (Release never reads it).</summary>
    public static Uri UrlFor(ModelTier tier)
    {
        var baseUrl = $"https://huggingface.co/{Repo}/resolve/main/";
#if DEBUG
        if (Environment.GetEnvironmentVariable("REMINDR_MODEL_BASE_URL") is { Length: > 0 } local && Uri.TryCreate(local, UriKind.Absolute, out _))
            baseUrl = local.TrimEnd('/') + "/";
#endif
        return new Uri(baseUrl + tier.File);
    }

    /// <summary>Hugging Face and its CDN (huggingface.co, hf.co and their sub-domains), https only. Debug builds also allow loopback.</summary>
    public static bool IsAllowedHost(Uri u)
    {
#if DEBUG
        if (u.IsLoopback) return true;
#endif
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return h is "huggingface.co" or "hf.co" || h.EndsWith(".huggingface.co") || h.EndsWith(".hf.co");
    }

    /// <summary>Total physical RAM of this PC in bytes (GlobalMemoryStatusEx).</summary>
    public static long TotalRam()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? (long)m.TotalPhys : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
