using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Remindr.Setup.Core;

public sealed class InstallOptions
{
    public bool DesktopShortcut { get; set; }
    public bool StartWithWindows { get; set; }
}

public sealed class ExistingInstall
{
    public InstallMode Mode { get; set; }
    public string? InstalledVersion { get; set; }
    public bool HasDesktopShortcut { get; set; }
    public bool HasRunValue { get; set; }
}

public static class RunningAppCloser
{
    public static readonly TimeSpan GraceTime = TimeSpan.FromSeconds(5);

    /// <summary>Makes sure Remindr is not running from the install folder: ask, close gracefully, then (with OK) force.</summary>
    public static void EnsureClosed(InstallPaths paths, IAppProcessGuard procs, ISetupPrompts prompts, ISetupLog log)
    {
        if (!procs.IsRunning(paths.ExePath)) return;
        log.Info("Remindr is running from the install folder.");
        if (!prompts.AskCloseRunningApp()) throw new SetupCancelledException("Remindr is still running.");

        if (procs.CloseGracefully(paths.ExePath, GraceTime)) { log.Info("Remindr closed."); return; }

        log.Info("Remindr did not close in time.");
        if (!prompts.AskForceClose()) throw new SetupCancelledException("Remindr is still running.");
        procs.ForceKill(paths.ExePath);
        if (procs.IsRunning(paths.ExePath)) throw new IOException("Remindr could not be closed.");
        log.Info("Remindr was force closed.");
    }
}

/// <summary>
/// Per-user install: extract to "&lt;dir&gt;.new", swap it in (old folder kept as "&lt;dir&gt;.old" until the end),
/// shortcuts, Run value, Uninstall key. Anything that fails is rolled back and rethrown.
/// %AppData%\Remindr is never touched.
/// </summary>
public sealed class InstallEngine
{
    private readonly InstallPaths _paths;
    private readonly AppIdentity _app;
    private readonly IRegistryStore _reg;
    private readonly IShortcutService _shortcuts;
    private readonly IAppProcessGuard _procs;
    private readonly ISetupLog _log;

    public InstallEngine(InstallPaths paths, AppIdentity app, IRegistryStore reg, IShortcutService shortcuts, IAppProcessGuard procs, ISetupLog log)
    {
        _paths = paths;
        _app = app;
        _reg = reg;
        _shortcuts = shortcuts;
        _procs = procs;
        _log = log;
    }

    public ExistingInstall Detect()
    {
        var installed = _reg.GetString(InstallPaths.UninstallSubKey, "DisplayVersion");
        var exists = _reg.KeyExists(InstallPaths.UninstallSubKey);
        return new ExistingInstall
        {
            InstalledVersion = installed,
            Mode = exists ? VersionCompare.Classify(installed ?? "", _app.Version) : InstallMode.Fresh,
            HasDesktopShortcut = File.Exists(_paths.DesktopShortcut),
            HasRunValue = _reg.GetString(InstallPaths.RunSubKey, InstallPaths.RunValueName) != null,
        };
    }

    /// <param name="openZip">Seekable stream of payload.zip.</param>
    /// <param name="openUninstallerSource">Stream whose bytes become uninstall.exe (the stub without payload).</param>
    public void Install(InstallOptions options, Func<Stream> openZip, Func<Stream> openUninstallerSource,
                        ISetupPrompts prompts, Action<SetupProgress> progress)
    {
        _log.Info("Install start: version " + _app.Version + " into " + _paths.InstallDir);
        var undo = new List<Action>();
        try
        {
            progress(new SetupProgress("Checking for a running Remindr", 0));
            RunningAppCloser.EnsureClosed(_paths, _procs, prompts, _log);

            // 1. Extract to <dir>.new
            progress(new SetupProgress("Extracting files", 0.02));
            DeleteDirectory(_paths.StagingDir);
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.StagingDir)!);

            long extracted;
            using (var zipStream = openZip())
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                extracted = ZipExtractor.ExtractAll(zip, _paths.StagingDir,
                    (done, total) => progress(new SetupProgress("Extracting files", 0.02 + 0.68 * (total == 0 ? 1 : (double)done / total))));

            var stagedExe = Path.Combine(_paths.StagingDir, InstallPaths.ExeName);
            if (!File.Exists(stagedExe)) throw new InvalidDataException("The setup payload does not contain " + InstallPaths.ExeName + ".");

            var stagedUninstaller = Path.Combine(_paths.StagingDir, InstallPaths.UninstallerName);
            using (var src = openUninstallerSource())
            using (var dst = new FileStream(stagedUninstaller, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                src.CopyTo(dst);
                extracted += dst.Length;
            }

            // 2. Swap it in. The old folder stays as <dir>.old so a failure can put it back.
            progress(new SetupProgress("Extracting files", 0.72));
            DeleteDirectory(_paths.BackupDir);
            var hadOld = Directory.Exists(_paths.InstallDir);
            if (hadOld) Directory.Move(_paths.InstallDir, _paths.BackupDir);
            undo.Add(() =>
            {
                DeleteDirectory(_paths.InstallDir);
                if (hadOld && Directory.Exists(_paths.BackupDir)) Directory.Move(_paths.BackupDir, _paths.InstallDir);
            });
            Directory.Move(_paths.StagingDir, _paths.InstallDir);
            _log.Info("Files in place (" + extracted + " bytes).");

            // 3. Shortcuts
            progress(new SetupProgress("Creating shortcuts", 0.78));
            CreateShortcut(_paths.StartMenuShortcut, undo);
            if (options.DesktopShortcut) CreateShortcut(_paths.DesktopShortcut, undo);
            else if (File.Exists(_paths.DesktopShortcut)) DeleteFileWithUndo(_paths.DesktopShortcut, undo);

            // 4. Registry
            progress(new SetupProgress("Registering app", 0.88));
            var oldUninstall = _reg.ReadAll(InstallPaths.UninstallSubKey);
            var oldRun = _reg.GetString(InstallPaths.RunSubKey, InstallPaths.RunValueName);
            undo.Add(() =>
            {
                if (oldUninstall is null) _reg.DeleteKeyTree(InstallPaths.UninstallSubKey);
                else _reg.SetValues(InstallPaths.UninstallSubKey, oldUninstall);
                if (oldRun is null) _reg.DeleteValue(InstallPaths.RunSubKey, InstallPaths.RunValueName);
                else _reg.SetValues(InstallPaths.RunSubKey, new[] { new KeyValuePair<string, object>(InstallPaths.RunValueName, oldRun) });
            });

            var sizeKb = (extracted + 1023) / 1024;
            _reg.SetValues(InstallPaths.UninstallSubKey, UninstallEntry.Build(_paths, _app, sizeKb, DateTime.Now));
            // While SetupFeatures.StartWithWindows is off, a stale Run value from an older install is removed.
            if (SetupFeatures.StartWithWindows && options.StartWithWindows)
                _reg.SetValues(InstallPaths.RunSubKey, new[] { new KeyValuePair<string, object>(InstallPaths.RunValueName, _paths.RunCommand) });
            else
                _reg.DeleteValue(InstallPaths.RunSubKey, InstallPaths.RunValueName);

            // 5. Done: drop the old copy (best effort).
            progress(new SetupProgress("Registering app", 0.97));
            try { DeleteDirectory(_paths.BackupDir); }
            catch (Exception ex) { _log.Error("Could not remove the old copy (ignored).", ex); }

            progress(new SetupProgress("Registering app", 1));
            _log.Info("Install finished.");
        }
        catch (Exception ex)
        {
            _log.Error("Install failed, rolling back.", ex);
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                try { undo[i](); }
                catch (Exception uex) { _log.Error("Rollback step failed.", uex); }
            }
            try { DeleteDirectory(_paths.StagingDir); } catch { /* best effort */ }
            throw;
        }
    }

    private void CreateShortcut(string lnk, List<Action> undo)
    {
        var existed = File.Exists(lnk);
        byte[]? old = existed ? File.ReadAllBytes(lnk) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);
        undo.Add(() =>
        {
            if (old is null) { if (File.Exists(lnk)) File.Delete(lnk); }
            else File.WriteAllBytes(lnk, old);
        });
        _shortcuts.Create(lnk, _paths.ExePath, null, _paths.InstallDir, "Remindr", _paths.ExePath, _app.Aumid);
    }

    private static void DeleteFileWithUndo(string path, List<Action> undo)
    {
        var old = File.ReadAllBytes(path);
        File.Delete(path);
        undo.Add(() => File.WriteAllBytes(path, old));
    }

    /// <summary>Recursive delete that tolerates a missing folder and brief file locks.</summary>
    public static void DeleteDirectory(string dir, string? keepFile = null)
    {
        if (!Directory.Exists(dir)) return;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (keepFile != null && string.Equals(Path.GetFullPath(f), Path.GetFullPath(keepFile), StringComparison.OrdinalIgnoreCase)) continue;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                if (keepFile == null) Directory.Delete(dir, true);
                else foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                        if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                return;
            }
            catch (Exception ex) when (attempt < 5 && (ex is IOException || ex is UnauthorizedAccessException))
            {
                System.Threading.Thread.Sleep(300);
            }
        }
    }
}
