namespace Remindr.Services.Assistant;

/// <summary>What the voice orb shows. Muted (gray) means voice input is off.</summary>
public enum OrbState { Idle, Listening, Thinking, Speaking, Muted }

/// <summary>The orb's rules, kept out of the control so they can be tested: which state wins, how a level is smoothed, and when it may move. UI-free.</summary>
public static class OrbModel
{
    /// <summary>Listening beats Speaking beats Thinking; with none of them, Idle (voice on) or Muted (voice off).</summary>
    public static OrbState Resolve(bool voiceOn, bool listening, bool speaking, bool thinking) =>
        listening ? OrbState.Listening : speaking ? OrbState.Speaking : thinking ? OrbState.Thinking : voiceOn ? OrbState.Idle : OrbState.Muted;

    /// <summary>One smoothing step: quick to rise (0.6), slower to fall (0.2). Input is clamped to 0..1.</summary>
    public static double Smooth(double previous, double raw)
    {
        raw = Math.Clamp(raw, 0, 1);
        return previous + (raw - previous) * (raw > previous ? 0.6 : 0.2);
    }

    /// <summary>Only Listening and Speaking follow the level; every other state rests at 0.</summary>
    public static double EffectiveLevel(OrbState state, double level) => state is OrbState.Listening or OrbState.Speaking ? Math.Clamp(level, 0, 1) : 0;

    /// <summary>Speech RMS (about 0.02-0.3) to 0..1 with a square-root curve, so quiet talking still moves the orb.</summary>
    public static double LevelFromRms(double rms) => Math.Clamp(Math.Sqrt(Math.Max(0, rms)) * 1.8, 0, 1);

    /// <summary>The blobs move only with effects on and never when muted; otherwise the orb is a static ring that changes colour per state.</summary>
    public static bool Moves(OrbState state, bool effectsEnabled) => effectsEnabled && state != OrbState.Muted;

    /// <summary>States a screen reader is told about as they happen.</summary>
    public static bool Announces(OrbState state) => state is OrbState.Listening or OrbState.Thinking or OrbState.Speaking;

    public static string NameKey(OrbState state) => "Voice_Orb_" + state;
}
