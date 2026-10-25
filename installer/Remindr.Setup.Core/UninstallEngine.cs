using System;
using System.IO;

namespace Remindr.Setup.Core;

/// <summary>
/// Removes the install folder, shortcuts, Run value, AppUserModelId key and (last, so a failed run can be retried)
/// the Uninstall key. %AppData%\Remindr is only removed (via IDataRemover, normally to the Recycle Bin) when removeData is true.
/// </summary>
public sealed class UninstallEngine
{
    private readonly InstallPaths _paths;
    private readonly AppIdentity _app;
    private readonly IRegistryStore _reg;
    private readonly IAppProcessGuard _procs;
    private readonly ISetupLog _log;

    private readonly IDataRemover _data;

    public UninstallEngine(InstallPaths paths, AppIdentity app, IRegistryStore reg, IAppProcessGuard procs, ISetupLog log, IDataRemover? dataRemover = null)
    {
        _data = dataRemover ?? new PermanentDataRemover();
        _paths = paths;
        _app = app;
        _reg = reg;
        _procs = procs;
        _log = log;
    }

    /// <param name="selfPath">The running uninstaller, if it sits inside the install folder: it is skipped (it cannot delete itself).</param>
    public void Uninstall(bool removeData, ISetupPrompts prompts, Action<SetupProgress> progress, string? selfPath = null)
    {
        _log.Info("Uninstall start (removeData=" + removeData + ").");

        progress(new SetupProgress("Closing Remindr", 0));
        RunningAppCloser.EnsureClosed(_paths, _procs, prompts, _log);

        progress(new SetupProgress("Removing files", 0.15));
        var inside = selfPath != null && IsInside(_paths.InstallDir, selfPath);
        InstallEngine.DeleteDirectory(_paths.InstallDir, inside ? selfPath : null);
        InstallEngine.DeleteDirectory(_paths.StagingDir);
        InstallEngine.DeleteDirectory(_paths.BackupDir);

        progress(new SetupProgress("Removing shortcuts", 0.5));
        DeleteFile(_paths.StartMenuShortcut);
        DeleteFile(_paths.DesktopShortcut);

        progress(new SetupProgress("Cleaning up settings", 0.65));
        _reg.DeleteValue(InstallPaths.RunSubKey, InstallPaths.RunValueName);
        _reg.DeleteKeyTree(InstallPaths.AumidSubKey(_app.Aumid));

        if (removeData)
        {
            progress(new SetupProgress("Removing your data", 0.8));
            if (Directory.Exists(_paths.DataDir)) _data.Remove(_paths.DataDir);
            _log.Info("Data folder removed.");
        }

        progress(new SetupProgress("Cleaning up settings", 0.95));
        _reg.DeleteKeyTree(InstallPaths.UninstallSubKey);

        progress(new SetupProgress("Cleaning up settings", 1));
        _log.Info("Uninstall finished.");
    }

    private void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.Error("Could not delete " + path + " (ignored).", ex); }
    }

    private static bool IsInside(string dir, string file) =>
        Path.GetFullPath(file).StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
