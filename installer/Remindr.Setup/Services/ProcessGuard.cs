using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>Finds Remindr.exe processes running from one exact path (a dev build or portable copy elsewhere is left alone).</summary>
public sealed class ProcessGuard : IAppProcessGuard
{
    private readonly ISetupLog _log;

    public ProcessGuard(ISetupLog log) { _log = log; }

    public bool IsRunning(string exePath)
    {
        var found = Find(exePath);
        foreach (var p in found) p.Dispose();
        return found.Count > 0;
    }

    public bool CloseGracefully(string exePath, TimeSpan timeout)
    {
        var procs = Find(exePath);
        try
        {
            foreach (var p in procs)
            {
                try { p.CloseMainWindow(); }
                catch (Exception ex) { _log.Error("CloseMainWindow failed.", ex); }
            }
            var deadline = DateTime.UtcNow + timeout;
            foreach (var p in procs)
            {
                var left = deadline - DateTime.UtcNow;
                if (left > TimeSpan.Zero) p.WaitForExit((int)left.TotalMilliseconds);
            }
        }
        finally { foreach (var p in procs) p.Dispose(); }
        return !IsRunning(exePath);
    }

    public void ForceKill(string exePath)
    {
        var procs = Find(exePath);
        foreach (var p in procs)
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch (Exception ex) { _log.Error("Kill failed.", ex); }
            finally { p.Dispose(); }
        }
    }

    private static List<Process> Find(string exePath)
    {
        var result = new List<Process>();
        var wanted = Path.GetFullPath(exePath);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path != null && string.Equals(Path.GetFullPath(path), wanted, StringComparison.OrdinalIgnoreCase)) { result.Add(p); continue; }
            }
            catch { /* another session or protected process: not ours */ }
            p.Dispose();
        }
        return result;
    }
}
