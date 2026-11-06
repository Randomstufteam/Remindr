using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Remindr.Services.Assistant;

/// <summary>
/// Runs llama.cpp's <c>llama-server.exe</c> with LFM3-Audio on 127.0.0.1 at a free port, behind a random API key, when voice is first used.
/// The graphics card is tried first (unless "Use graphics card" is Off) and the CPU when the GPU start fails. The process lives in a job object,
/// so it dies with Remindr even after a crash. <see cref="Stop"/> ends it (voice off, files removed, app exit); the next request starts it again.
/// </summary>
public sealed class LfmAudioServer : IDisposable
{
    private readonly string _modelsDir;
    private readonly Func<GpuMode> _gpu;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _startTimeout;
    private Process? _process;
    private HttpClient? _http;
    private JobHandle? _job;

    public LfmAudioServer(string modelsDir, Func<GpuMode> gpu, TimeSpan? startTimeout = null)
    {
        _modelsDir = modelsDir;
        _gpu = gpu;
        _startTimeout = startTimeout ?? TimeSpan.FromMinutes(2);
    }

    /// <summary>"GPU" or "CPU" once started (the self-check report), null before.</summary>
    public string? Device { get; private set; }

    /// <summary>A client for the running server (base address and key set), starting it if needed.</summary>
    public async Task<HttpClient> ClientAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_http is not null && _process is { HasExited: false }) return _http;
            StopLocked();
            var exe = EnsureRuntime();
            if (_gpu() != GpuMode.Off && await TryStartAsync(exe, gpu: true, ct).ConfigureAwait(false)) return _http!;
            if (await TryStartAsync(exe, gpu: false, ct).ConfigureAwait(false)) return _http!;
            throw new InvalidOperationException("The voice model could not start.");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Unpacks the verified runtime zip once. ExtractToDirectory refuses entries that point outside the folder.</summary>
    private string EnsureRuntime()
    {
        var dir = AudioModelCatalog.RuntimeDir(_modelsDir);
        var exe = Path.Combine(dir, "llama-server.exe");
        if (File.Exists(exe)) return exe;
        var zip = ModelCatalog.PathOf(AudioModelCatalog.Runtime, _modelsDir);
        if (!File.Exists(zip)) throw new FileNotFoundException("The voice model is not installed.");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ZipFile.ExtractToDirectory(zip, dir);
        // The release zip may keep the binaries in a sub-folder.
        var found = Directory.GetFiles(dir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException("llama-server.exe is missing from the voice runtime.");
        return found;
    }

    private async Task<bool> TryStartAsync(string exe, bool gpu, CancellationToken ct)
    {
        var port = FreePort();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in new[] { "-m", ModelCatalog.PathOf(AudioModelCatalog.Model, _modelsDir), "-mm", ModelCatalog.PathOf(AudioModelCatalog.Projector, _modelsDir),
                     "--host", "127.0.0.1", "--port", port.ToString(), "--no-webui" }) psi.ArgumentList.Add(a);
        if (!gpu) { psi.ArgumentList.Add("--device"); psi.ArgumentList.Add("none"); }
        psi.Environment["LLAMA_API_KEY"] = key;   // not on the command line, where other processes could read it

        var process = Process.Start(psi) ?? throw new InvalidOperationException("llama-server did not start.");
        _job ??= JobHandle.Create();
        _job?.Add(process);
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", key);
        var healthy = false;
        try { healthy = await WaitHealthyAsync(process, http, ct).ConfigureAwait(false); }
        finally
        {
            // Cancelled (Esc) or failed while starting: don't leave a half-started server holding 2 GB.
            if (!healthy) { http.Dispose(); Kill(process); }
        }
        if (!healthy) return false;
        _process = process;
        _http = http;
        Device = gpu ? "GPU" : "CPU";
        return true;
    }

    private async Task<bool> WaitHealthyAsync(Process process, HttpClient http, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _startTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited) return false;
            try
            {
                using var r = await http.GetAsync("health", ct).ConfigureAwait(false);
                if (r.StatusCode == HttpStatusCode.OK) return true;
            }
            catch (HttpRequestException) { /* not listening yet */ }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return false;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } } catch { /* already gone */ }
        p.Dispose();
    }

    /// <summary>Ends the server and frees the model (about 2 GB). Waits for the process to exit so its files can be deleted right after.</summary>
    public void Stop()
    {
        _gate.Wait();
        try { StopLocked(); }
        finally { _gate.Release(); }
    }

    private void StopLocked()
    {
        _http?.Dispose();
        _http = null;
        if (_process is { } p) Kill(p);
        _process = null;
        Device = null;
    }

    public void Dispose()
    {
        Stop();
        _job?.Dispose();
        _gate.Dispose();
    }

    /// <summary>A Windows job object with KILL_ON_JOB_CLOSE: when Remindr exits for any reason, Windows ends the server too.</summary>
    private sealed class JobHandle : IDisposable
    {
        private readonly IntPtr _h;
        private JobHandle(IntPtr h) => _h = h;

        public static JobHandle? Create()
        {
            var h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return null;
            var info = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 /* JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE */ } };
            if (!SetInformationJobObject(h, 9 /* JobObjectExtendedLimitInformation */, ref info, (uint)Marshal.SizeOf<ExtendedLimits>())) { CloseHandle(h); return null; }
            return new JobHandle(h);
        }

        public void Add(Process p) { try { AssignProcessToJobObject(_h, p.Handle); } catch { /* best effort: Stop still kills it */ } }
        public void Dispose() => CloseHandle(_h);

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}

/// <summary>Speech to text with LFM3-Audio: the samples go to the local server as an in-memory WAV, in an OpenAI-style chat request.</summary>
public sealed class LfmTranscriber : ITranscriber
{
    private readonly LfmAudioServer _server;
    public LfmTranscriber(LfmAudioServer server) => _server = server;

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        var http = await _server.ClientAsync(ct).ConfigureAwait(false);
        var wav = Wav.FromSamples(samples, 16000);
        try
        {
            var body = new
            {
                temperature = 0,
                messages = new object[]
                {
                    new { role = "system", content = "Perform ASR in English." },
                    new { role = "user", content = new object[] { new { type = "input_audio", input_audio = new { data = Convert.ToBase64String(wav), format = "wav" } } } },
                },
            };
            using var r = await http.PostAsJsonAsync("v1/chat/completions", body, ct).ConfigureAwait(false);
            r.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        finally { Array.Clear(wav); }   // the base64 copy is a string and is left to the GC
    }
}

/// <summary>16-bit mono PCM WAV in memory.</summary>
public static class Wav
{
    public static byte[] FromSamples(float[] samples, int rate)
    {
        var data = samples.Length * 2;
        var b = new byte[44 + data];
        using var w = new BinaryWriter(new MemoryStream(b));
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        return b;
    }
}
