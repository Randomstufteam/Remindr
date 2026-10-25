using System;
using System.IO;

namespace Remindr.Setup.Core;

/// <summary>Command line of the setup stub. Switches may start with /, - or --, in any case.</summary>
public sealed class SetupArgs
{
    public bool Uninstall { get; private set; }
    public bool Quiet { get; private set; }
    /// <summary>Fake run that writes nothing (screenshots, UI testing).</summary>
    public bool Demo { get; private set; }
    /// <summary>With Demo: pretend an older version is installed (shows the Update variant).</summary>
    public bool DemoUpdate { get; private set; }
    /// <summary>The uninstaller is running from its temp copy (so it may delete the install folder).</summary>
    public bool FromTemp { get; private set; }
    public bool DesktopShortcut { get; private set; }
    public bool StartWithWindows { get; private set; }
    public bool RemoveData { get; private set; }

    /// <param name="exePath">Path of the running exe: a file named uninstall.exe means uninstall mode.</param>
    public static SetupArgs Parse(string[] args, string? exePath = null)
    {
        var r = new SetupArgs();
        foreach (var raw in args)
        {
            var a = raw.Trim().TrimStart('/', '-').ToLowerInvariant();
            switch (a)
            {
                case "uninstall": r.Uninstall = true; break;
                case "quiet": case "silent": case "q": r.Quiet = true; break;
                case "demo": r.Demo = true; break;
                case "demo-update": r.Demo = true; r.DemoUpdate = true; break;
                case "fromtemp": r.FromTemp = true; break;
                case "desktop": case "desktopshortcut": r.DesktopShortcut = true; break;
                case "startup": r.StartWithWindows = true; break;
                case "removedata": r.RemoveData = true; break;
            }
        }
        if (exePath is not null &&
            string.Equals(Path.GetFileName(exePath), InstallPaths.UninstallerName, StringComparison.OrdinalIgnoreCase))
            r.Uninstall = true;
        return r;
    }
}
