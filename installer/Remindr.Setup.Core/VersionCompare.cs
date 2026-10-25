using System;

namespace Remindr.Setup.Core;

public enum InstallMode
{
    /// <summary>Nothing installed yet.</summary>
    Fresh,
    /// <summary>An older version is installed: upgrade in place.</summary>
    Update,
    /// <summary>The same version is installed.</summary>
    Reinstall,
    /// <summary>A newer version is installed than the one in this setup.</summary>
    Downgrade,
}

public static class VersionCompare
{
    /// <summary>Parses "0.3.0", "0.3.0.0" or "0.3.0-beta" (suffix ignored). Null when unparseable.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = text!.Trim();
        var cut = core.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) core = core.Substring(0, cut);
        if (!Version.TryParse(core, out var v)) return null;
        // Version("0.3") has Build -1; normalise to 4 parts so 0.3 == 0.3.0.0.
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    /// <param name="installed">DisplayVersion from the Uninstall key, or null when the key is missing.</param>
    public static InstallMode Classify(string? installed, string current)
    {
        if (installed is null) return InstallMode.Fresh;
        var have = Parse(installed);
        var want = Parse(current);
        if (have is null || want is null) return InstallMode.Update;   // unknown old value: just replace it
        var c = have.CompareTo(want);
        return c < 0 ? InstallMode.Update : c == 0 ? InstallMode.Reinstall : InstallMode.Downgrade;
    }
}
