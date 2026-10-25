using System;
using System.IO;

namespace Remindr.Setup.Core;

/// <summary>Every path and registry location setup touches, in one place. Tests build one over a temp folder.</summary>
public sealed class InstallPaths
{
    public const string ExeName = "Remindr.exe";
    public const string UninstallerName = "uninstall.exe";
    public const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Remindr";
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "Remindr";
    public const string MinimizedArg = "--minimized";

    public InstallPaths(string localAppData, string appData, string startMenuPrograms, string desktop)
    {
        InstallDir = Path.Combine(localAppData, "Programs", "Remindr");
        StagingDir = InstallDir + ".new";
        BackupDir = InstallDir + ".old";
        ExePath = Path.Combine(InstallDir, ExeName);
        UninstallerPath = Path.Combine(InstallDir, UninstallerName);
        StartMenuShortcut = Path.Combine(startMenuPrograms, "Remindr.lnk");
        DesktopShortcut = Path.Combine(desktop, "Remindr.lnk");
        DataDir = Path.Combine(appData, "Remindr");
        ToastCompatDir = Path.Combine(localAppData, "ToastNotificationManagerCompat", "Apps", "Remindr.App");
    }

    public string InstallDir { get; }
    public string StagingDir { get; }
    public string BackupDir { get; }
    public string ExePath { get; }
    public string UninstallerPath { get; }
    public string StartMenuShortcut { get; }
    public string DesktopShortcut { get; }
    /// <summary>%AppData%\Remindr: profiles, reminders, avatars. Only removed when the user asks.</summary>
    public string DataDir { get; }
    /// <summary>%LocalAppData%\ToastNotificationManagerCompat\Apps\Remindr.App: the toast library's per-app leftovers.</summary>
    public string ToastCompatDir { get; }

    /// <summary>COM activator class the toast library registers for Remindr's toast clicks.</summary>
    public const string ToastClsidSubKey = @"Software\Classes\CLSID\{52656d69-6e64-722e-4170-700000000000}";

    public static string AumidSubKey(string aumid) => @"Software\Classes\AppUserModelId\" + aumid;

    /// <summary>"C:\...\Remindr.exe" --minimized</summary>
    public string RunCommand => "\"" + ExePath + "\" " + MinimizedArg;
    public string UninstallString => "\"" + UninstallerPath + "\" /uninstall";
    public string QuietUninstallString => "\"" + UninstallerPath + "\" /uninstall /quiet";

    public static InstallPaths FromEnvironment() => new InstallPaths(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
}
