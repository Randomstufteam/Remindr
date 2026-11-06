using Microsoft.Win32;
using NAudio.Wave;

namespace Remindr.Services.Assistant;

/// <summary>Whether Windows' privacy setting blocks the microphone for desktop apps (or for everyone).</summary>
public static class MicPermission
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    public static bool IsBlocked() =>
        Denied(Registry.LocalMachine, Key) || Denied(Registry.CurrentUser, Key) || Denied(Registry.CurrentUser, Key + @"\NonPackaged");

    private static bool Denied(RegistryKey root, string sub)
    {
        try { using var k = root.OpenSubKey(sub); return k?.GetValue("Value") as string == "Deny"; }
        catch { return false; }
    }
}

/// <summary>Microphone capture through NAudio's WaveInEvent at 16 kHz mono. Samples are kept in a memory list only (60 s at most); nothing touches the disk.</summary>
public sealed class NAudioRecorder : IRecorder
{
    private const int MaxSamples = 16000 * 60;
    private readonly object _lock = new();
    private List<float> _samples = new();
    private WaveInEvent? _wave;

    public void Start(Action<double> rms)
    {
        if (MicPermission.IsBlocked()) throw new MicException(VoiceError.AccessDenied);
        if (WaveInEvent.DeviceCount == 0) throw new MicException(VoiceError.NoMicrophone);
        lock (_lock) _samples = new List<float>(16000 * 10);
        try
        {
            var wave = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 33 };
            wave.DataAvailable += (_, e) =>
            {
                var n = e.BytesRecorded / 2;
                if (n == 0) return;
                double sum = 0;
                lock (_lock)
                {
                    for (var i = 0; i < n; i++)
                    {
                        var s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                        sum += s * s;
                        if (_samples.Count < MaxSamples) _samples.Add(s);
                    }
                }
                rms(Math.Sqrt(sum / n));
            };
            wave.StartRecording();
            _wave = wave;
        }
        catch (NAudio.MmException ex) { throw new MicException(VoiceError.NoMicrophone, ex); }
    }

    public float[] Stop()
    {
        Close();
        lock (_lock) { var all = _samples.ToArray(); _samples = new(); return all; }
    }

    public void Abort()
    {
        Close();
        lock (_lock) _samples = new();
    }

    private void Close()
    {
        var w = _wave;
        _wave = null;
        if (w is null) return;
        try { w.StopRecording(); } catch { /* already stopped */ }
        w.Dispose();
    }
}
