using System.Text.RegularExpressions;

namespace Remindr.Services.Assistant;

public enum VoiceError { NoMicrophone, AccessDenied, Failed, NothingHeard }

/// <summary>The microphone could not be used; <see cref="Kind"/> says why.</summary>
public sealed class MicException : Exception
{
    public MicException(VoiceError kind, Exception? inner = null) : base(kind.ToString(), inner) => Kind = kind;
    public VoiceError Kind { get; }
}

/// <summary>Captures 16 kHz mono audio in memory only. The mic is open between <see cref="Start"/> and <see cref="Stop"/>/<see cref="Abort"/>.</summary>
public interface IRecorder
{
    /// <summary>Opens the mic; <paramref name="rms"/> gets the RMS (0..1) of each buffer, about 30 times a second. Throws <see cref="MicException"/>.</summary>
    void Start(Action<double> rms);
    /// <summary>Closes the mic and returns what was captured.</summary>
    float[] Stop();
    /// <summary>Closes the mic and drops what was captured.</summary>
    void Abort();
}

public interface ITranscriber
{
    Task<string> TranscribeAsync(float[] samples, CancellationToken ct);
}

public enum PttState { Idle, Recording, Transcribing }
public enum PttSource { Key, Button }

/// <summary>
/// Push-to-talk: hold (Ctrl+Space) or toggle (the orb / mic button) to record, release to transcribe. A hold started by the key ends only by the key
/// and a toggle only by the button. Esc or losing focus cancels (nothing is transcribed). The samples live in memory only and are zeroed once the
/// transcript is back. UI-free; events can fire on any thread.
/// </summary>
public sealed class PushToTalkController
{
    /// <summary>Under 0.3 s of audio is an accidental tap.</summary>
    public const int MinSamples = 16000 * 3 / 10;
    /// <summary>A button-started recording can be forgotten; the mic closes on its own after this.</summary>
    public TimeSpan MaxRecording { get; set; } = TimeSpan.FromSeconds(60);
    private int _take;

    private readonly IRecorder _recorder;
    private readonly ITranscriber _transcriber;
    private PttSource _source;
    private CancellationTokenSource? _cts;

    public PushToTalkController(IRecorder recorder, ITranscriber transcriber)
    {
        _recorder = recorder;
        _transcriber = transcriber;
    }

    public PttState State { get; private set; }
    /// <summary>The transcription in flight (for tests).</summary>
    public Task? Pending { get; private set; }

    public event EventHandler? StateChanged;
    /// <summary>The live mic level, 0..1.</summary>
    public event Action<double>? Level;
    public event Action<string>? Transcribed;
    public event Action<VoiceError>? Failed;

    private void Set(PttState s) { State = s; StateChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>Starts recording. Key repeat while recording, or a start during transcription, is ignored.</summary>
    public bool Begin(PttSource source)
    {
        if (State != PttState.Idle) return false;
        try { _recorder.Start(rms => Level?.Invoke(OrbModel.LevelFromRms(rms))); }
        catch (MicException ex) { Failed?.Invoke(ex.Kind); return false; }
        catch (Exception ex) { ErrorLog.Write(ex, "Microphone"); Failed?.Invoke(VoiceError.Failed); return false; }
        _source = source;
        Set(PttState.Recording);
        var take = ++_take;
        _ = Task.Delay(MaxRecording).ContinueWith(_ => { if (_take == take) End(source); }, TaskScheduler.Default);
        return true;
    }

    /// <summary>Button click: starts when idle, finishes when the button started it.</summary>
    public void Toggle()
    {
        if (State == PttState.Idle) Begin(PttSource.Button);
        else End(PttSource.Button);
    }

    /// <summary>Release: stops the mic and transcribes. Ignored unless recording and started by the same <paramref name="source"/>.</summary>
    public void End(PttSource source)
    {
        if (State != PttState.Recording || _source != source) return;
        var samples = _recorder.Stop();
        Level?.Invoke(0);
        if (samples.Length < MinSamples)
        {
            Array.Clear(samples);
            Set(PttState.Idle);
            Failed?.Invoke(VoiceError.NothingHeard);
            return;
        }
        Set(PttState.Transcribing);
        _cts = new CancellationTokenSource();
        Pending = RunAsync(samples, _cts);
    }

    /// <summary>Esc or focus lost: drops the recording, or the transcript still being made.</summary>
    public void Cancel()
    {
        if (State == PttState.Recording)
        {
            _recorder.Abort();
            Level?.Invoke(0);
            Set(PttState.Idle);
        }
        else if (State == PttState.Transcribing)
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { /* the transcript arrived meanwhile */ }
        }
    }

    private async Task RunAsync(float[] samples, CancellationTokenSource cts)
    {
        var text = "";
        VoiceError? error = null;
        try { text = Clean(await _transcriber.TranscribeAsync(samples, cts.Token).ConfigureAwait(false)); }
        catch (OperationCanceledException) { text = ""; }
        catch (Exception ex) { ErrorLog.Write(ex, "Voice transcription"); error = VoiceError.Failed; }
        finally
        {
            Array.Clear(samples);   // audio never outlives the transcription
            var cancelled = cts.IsCancellationRequested;
            if (_cts == cts) _cts = null;
            Set(PttState.Idle);
            cts.Dispose();
            if (!cancelled)
            {
                if (error is { } e) Failed?.Invoke(e);
                else if (text.Length == 0) Failed?.Invoke(VoiceError.NothingHeard);
                else Transcribed?.Invoke(text);
            }
        }
    }

    /// <summary>Drops Whisper's non-speech tags such as [BLANK_AUDIO] or (music) and tidies the spaces.</summary>
    public static string Clean(string? raw) => Regex.Replace(Regex.Replace(raw ?? "", @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", " "), @"\s+", " ").Trim();
}
