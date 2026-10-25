using System;
using System.Diagnostics;
using System.IO;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>
/// A running exe cannot delete itself, and MoveFileEx(delay until reboot) needs admin, so:
/// the installed uninstall.exe copies itself to %TEMP% and relaunches from there (so the install folder is free to delete),
/// and whichever copy is left over at the end is removed by a detached "cmd /c ping ... &amp; del" a few seconds after exit.
/// </summary>
public static class SelfDelete
{
    /// <summary>Set once an uninstall that ran from inside the install folder has finished removing everything else.</summary>
    public static bool InPlaceUninstallDone { get; set; }

    public static bool IsInside(string dir, string file) =>
        Path.GetFullPath(file).StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when the temp copy was started (the caller should then exit at once, releasing the install folder).</summary>
    public static bool TryRelaunchFromTemp(SetupArgs args, ISetupLog log)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Remindr-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, InstallPaths.UninstallerName);
            File.Copy(SetupInfo.ExePath, copy, true);

            var a = "/uninstall /fromtemp" + (args.RemoveData ? " /removedata" : "");
            Process.Start(new ProcessStartInfo(copy, a) { UseShellExecute = false, WorkingDirectory = dir });
            log.Info("Relaunched uninstaller from " + copy);
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Could not relaunch from temp; running in place.", ex);
            return false;
        }
    }

    /// <summary>Starts a hidden, detached cmd that waits for this process to exit, then deletes the exe and (if empty) its folder.</summary>
    public static void Schedule(string exePath, ISetupLog log)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath)!;
            var cmd = "/c ping -n 4 127.0.0.1 >nul & del /f /q \"" + exePath + "\" & rmdir \"" + dir + "\"";
            // Not the temp folder itself: a process's working directory cannot be removed.
            var cwd = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = cwd });
            log.Info("Scheduled delete of " + exePath);
        }
        catch (Exception ex) { log.Error("Could not schedule self delete.", ex); }
    }
}
