namespace Remindr.Setup.Core;

/// <summary>Switches for installer features that are built but not shipped yet.</summary>
public static class SetupFeatures
{
    /// <summary>
    /// "Start with Windows" (HKCU Run value). Off for v0.3: the app has no notifications or background running
    /// and does not understand --minimized yet (user decision 2026-10-25). Returns with PLAN Phase 4.
    /// While off, install/update never writes the Run value and removes a stale one.
    /// (static readonly rather than const so the gated code does not become unreachable-code warnings.)
    /// </summary>
    public static readonly bool StartWithWindows = false;
}
