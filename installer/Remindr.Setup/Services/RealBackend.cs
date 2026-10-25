using System;
using System.Diagnostics;
using System.IO;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>The real install and uninstall: wires the Core engines to the registry, shell links and process APIs.</summary>
public sealed class RealBackend : ISetupBackend
{
    private readonly ISetupLog _log;
    private readonly InstallPaths _paths = InstallPaths.FromEnvironment();
    private readonly WinRegistryStore _reg = new WinRegistryStore();

    public RealBackend(ISetupLog log) { _log = log; }

    public bool IsDemo => false;
    public string InstallDir => _paths.InstallDir;
    public string DataDir => _paths.DataDir;

    private InstallEngine Installer() => new InstallEngine(_paths, SetupInfo.Identity, _reg, new ShellLinkService(), new ProcessGuard(_log), _log);

    public ExistingInstall Detect()
    {
        try { return Installer().Detect(); }
        catch (Exception ex)
        {
            _log.Error("Could not read the existing install (treating as fresh).", ex);
            return new ExistingInstall();
        }
    }

    public void Install(InstallOptions options, ISetupPrompts prompts, Action<SetupProgress> progress)
    {
        using var self = new FileStream(SetupInfo.ExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var payload = PayloadInfo.TryRead(self);
        if (payload == null)
            throw new InvalidDataException("This setup file has no program files attached (it was not built with build-installer.ps1).");

        Installer().Install(options,
            () => new SubStream(self, payload.StubLength, payload.ZipLength),
            () => new SubStream(self, 0, payload.StubLength),
            prompts, progress);
    }

    public void Uninstall(bool removeData, ISetupPrompts prompts, Action<SetupProgress> progress)
    {
        var engine = new UninstallEngine(_paths, SetupInfo.Identity, _reg, new ProcessGuard(_log), _log, new RecycleBinDataRemover());
        engine.Uninstall(removeData, prompts, progress, SetupInfo.ExePath);
        if (SelfDelete.IsInside(_paths.InstallDir, SetupInfo.ExePath)) SelfDelete.InPlaceUninstallDone = true;
    }

    public void LaunchApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_paths.ExePath) { UseShellExecute = true, WorkingDirectory = _paths.InstallDir });
        }
        catch (Exception ex) { _log.Error("Could not launch Remindr.", ex); }
    }
}
