using System.IO;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
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
        ct.ThrowIfCancellationRequested();   // a token cancelled before Play would have stopped nothing and the audio would play anyway
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
/// The natural voice (LFM3-Audio through <see cref="LfmAudioServer"/>) once it is downloaded; a Windows voice otherwise, or when the user picked one.
/// A reply is spoken sentence by sentence and the next sentence is made while one plays, so speech starts after the first sentence.
/// </summary>
public sealed class LfmSpeaker : ISpeaker
{
    private readonly LfmAudioServer _server;
    private readonly Func<bool> _installed;
    private readonly ISpeaker _windows;

    public LfmSpeaker(LfmAudioServer server, Func<bool> installed, ISpeaker windows)
    {
        _server = server;
        _installed = installed;
        _windows = windows;
    }

    public IReadOnlyList<string> Voices() => _windows.Voices();

    /// <summary>The LFM voice for a stored name: the default for null (or a Kokoro voice from 0.5.0), the id for a <see cref="AudioModelCatalog.Prefix"/> name, null for a Windows voice.</summary>
    public static string? NaturalId(string? voiceName) => voiceName is null || voiceName.StartsWith("kokoro:", StringComparison.Ordinal) ? AudioModelCatalog.Voices[0].Id
        : voiceName.StartsWith(AudioModelCatalog.Prefix, StringComparison.Ordinal) ? voiceName[AudioModelCatalog.Prefix.Length..] : null;

    /// <summary>Sentences, so the first can play while the rest are made.</summary>
    public static IReadOnlyList<string> Sentences(string text) =>
        Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Where(s => s.Length > 0).ToList();

    public Task SpeakAsync(string text, string? voiceName, Action<double>? level, CancellationToken ct)
    {
        var id = NaturalId(voiceName);
        // A natural voice picked earlier whose model was removed falls back to the Windows default.
        if (!_installed() || id is null) return _windows.SpeakAsync(text, id is null ? voiceName : null, level, ct);
        return SpeakNaturalAsync(id, text, level, ct);
    }

    private async Task SpeakNaturalAsync(string id, string text, Action<double>? level, CancellationToken ct)
    {
        var parts = Sentences(text);
        if (parts.Count == 0) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var next = RenderAsync(parts[0], id, stop.Token);
        try
        {
            for (var i = 0; i < parts.Count; i++)
            {
                var wav = await next.ConfigureAwait(false);
                if (i + 1 < parts.Count) next = RenderAsync(parts[i + 1], id, stop.Token);
                using var reader = new WaveFileReader(new MemoryStream(wav));
                await SpeechAudio.PlayAsync(reader, level, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Cancelled or failed mid-reply: don't leave the next sentence rendering in the background.
            stop.Cancel();
            try { await next.ConfigureAwait(false); } catch { /* cancelled */ }
        }
    }

    /// <summary>The WAV for <paramref name="text"/>, not played (also the self-check).</summary>
    public async Task<byte[]> RenderAsync(string text, string? voiceId, CancellationToken ct)
    {
        var http = await _server.ClientAsync(ct).ConfigureAwait(false);
        using var r = await http.PostAsJsonAsync("tts", new { input = text, voice = voiceId ?? AudioModelCatalog.Voices[0].Id, lang = "en" }, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}
