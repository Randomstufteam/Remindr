using System.IO;
using System.Text.RegularExpressions;
using KokoroSharp;
using KokoroSharp.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.SpeechSynthesis;

namespace Remindr.Services.Assistant;

/// <summary>Reads text aloud. <c>level</c> gets the output level (0..1) while it plays.</summary>
public interface ISpeaker
{
    Task SpeakAsync(string text, string? voiceName, Action<double>? level, CancellationToken ct);
    /// <summary>Display names of the installed English Windows voices.</summary>
    IReadOnlyList<string> Voices();
}

/// <summary>Plays audio with NAudio and reports the peak of each ~30 ms block, so the orb follows the real output. Cancelling stops it at once.</summary>
internal static class SpeechAudio
{
    public static async Task PlayAsync(IWaveProvider audio, Action<double>? level, CancellationToken ct)
    {
        var meter = new MeteringSampleProvider(audio.ToSampleProvider(), Math.Max(1, audio.WaveFormat.SampleRate / 30));
        meter.StreamVolume += (_, e) => level?.Invoke(OrbModel.LevelFromRms(e.MaxSampleValues.Length > 0 ? e.MaxSampleValues.Max() * 0.5 : 0));
        using var output = new WaveOutEvent();
        var done = new TaskCompletionSource();
        output.PlaybackStopped += (_, _) => done.TrySetResult();
        output.Init(meter);
        using var reg = ct.Register(() => output.Stop());
        output.Play();
        try { await done.Task; }
        finally { level?.Invoke(0); }
        ct.ThrowIfCancellationRequested();
    }
}

/// <summary>Windows' built-in text to speech (Windows.Media.SpeechSynthesis, nothing to download), rendered to memory and played with NAudio.</summary>
public sealed class WindowsSpeaker : ISpeaker
{
    public IReadOnlyList<string> Voices()
    {
        try
        {
            return SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                .Select(v => v.DisplayName).Distinct().OrderBy(n => n).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public async Task SpeakAsync(string text, string? voiceName, Action<double>? level, CancellationToken ct)
    {
        using var synth = new SpeechSynthesizer();
        if (voiceName is not null && SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == voiceName) is { } voice) synth.Voice = voice;
        using var rendered = await synth.SynthesizeTextToStreamAsync(text);
        ct.ThrowIfCancellationRequested();
        var wav = new MemoryStream();
        await rendered.AsStreamForRead().CopyToAsync(wav, ct);
        wav.Position = 0;
        using var reader = new WaveFileReader(wav);
        await SpeechAudio.PlayAsync(reader, level, ct);
    }
}

/// <summary>
/// The natural voice (Kokoro-82M through KokoroSharp, on the CPU) once one of its sizes is downloaded; a Windows voice otherwise, or when the user
/// picked one. A reply is spoken sentence by sentence and the next sentence is made while one plays, so speech starts after the first sentence.
/// The model loads on first use (about 1.5 s) and stays loaded until <see cref="Unload"/>.
/// </summary>
public sealed class NaturalSpeaker : ISpeaker
{
    private static readonly WaveFormat Pcm = new(24000, 16, 1);
    private readonly Func<string?> _modelPath;
    private readonly ISpeaker _windows;
    private readonly object _lock = new();
    private KokoroWavSynthesizer? _synth;
    private string? _loadedPath;
    private Task _busy = Task.CompletedTask;

    public NaturalSpeaker(Func<string?> modelPath, ISpeaker windows)
    {
        _modelPath = modelPath;
        _windows = windows;
    }

    public IReadOnlyList<string> Voices() => _windows.Voices();

    /// <summary>The Kokoro voice id for a stored name: the default for null, the id for a <see cref="KokoroCatalog.Prefix"/> name, null for a Windows voice.</summary>
    public static string? KokoroId(string? voiceName) => voiceName is null ? KokoroCatalog.Voices[0].Id
        : voiceName.StartsWith(KokoroCatalog.Prefix, StringComparison.Ordinal) ? voiceName[KokoroCatalog.Prefix.Length..] : null;

    /// <summary>Sentences, so the first can play while the rest are made.</summary>
    public static IReadOnlyList<string> Sentences(string text) =>
        Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Where(s => s.Length > 0).ToList();

    public Task SpeakAsync(string text, string? voiceName, Action<double>? level, CancellationToken ct)
    {
        var path = _modelPath();
        var id = KokoroId(voiceName);
        // A natural voice picked earlier whose model was removed falls back to the Windows default.
        if (path is null || id is null) return _windows.SpeakAsync(text, id is null ? voiceName : null, level, ct);
        Task run;
        lock (_lock) _busy = run = SpeakNaturalAsync(path, id, text, level, ct);
        return run;
    }

    private async Task SpeakNaturalAsync(string path, string id, string text, Action<double>? level, CancellationToken ct)
    {
        var synth = await Task.Run(() => Load(path), ct).ConfigureAwait(false);
        var voice = KokoroVoiceManager.GetVoice(id);
        var parts = Sentences(text);
        if (parts.Count == 0) return;
        var next = synth.SynthesizeAsync(parts[0], voice);
        for (var i = 0; i < parts.Count; i++)
        {
            var pcm = await next.WaitAsync(ct).ConfigureAwait(false);
            if (i + 1 < parts.Count) next = synth.SynthesizeAsync(parts[i + 1], voice);
            await SpeechAudio.PlayAsync(new RawSourceWaveStream(new MemoryStream(pcm), Pcm), level, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The 16-bit 24 kHz mono audio for <paramref name="text"/>, not played (the self-check).</summary>
    public async Task<byte[]> RenderAsync(string path, string text, string? voiceName)
    {
        var synth = await Task.Run(() => Load(path)).ConfigureAwait(false);
        return await synth.SynthesizeAsync(text, KokoroVoiceManager.GetVoice(KokoroId(voiceName) ?? KokoroCatalog.Voices[0].Id)).ConfigureAwait(false);
    }

    private KokoroWavSynthesizer Load(string path)
    {
        lock (_lock)
        {
            if (_synth is not null && _loadedPath == path) return _synth;
            _synth?.Dispose();
            if (KokoroVoiceManager.Voices.Count == 0) KokoroVoiceManager.LoadVoicesFromPath(Path.Combine(AppContext.BaseDirectory, "voices"));
            _synth = KokoroWavSynthesizer.LoadModel(path);
            _loadedPath = path;
            return _synth;
        }
    }

    /// <summary>Frees the model (about 1 GB of RAM) once the reply being spoken is done. Voice off or the model removed.</summary>
    public void Unload()
    {
        KokoroWavSynthesizer? old;
        Task busy;
        lock (_lock) { old = _synth; _synth = null; _loadedPath = null; busy = _busy; }
        if (old is not null) _ = busy.ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
    }
}
