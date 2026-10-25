using System;
using System.Collections.Generic;

namespace Remindr.Setup.Core;

/// <summary>HKCU-only registry access. The real one lives in the setup stub; tests use a fake.</summary>
public interface IRegistryStore
{
    bool KeyExists(string subKey);
    /// <summary>All values of the key (string or int), or null when the key does not exist.</summary>
    IDictionary<string, object>? ReadAll(string subKey);
    string? GetString(string subKey, string name);
    /// <summary>Creates the key if needed and sets the values (string or int).</summary>
    void SetValues(string subKey, IEnumerable<KeyValuePair<string, object>> values);
    void DeleteValue(string subKey, string name);
    void DeleteKeyTree(string subKey);
}

public interface IShortcutService
{
    /// <summary>Creates a .lnk. When aumid is not null it is stored as System.AppUserModel.ID.</summary>
    void Create(string lnkPath, string targetPath, string? arguments, string workingDir, string description, string? iconPath, string? aumid);
}

public interface IAppProcessGuard
{
    /// <summary>True when a process is running from exactly this exe path.</summary>
    bool IsRunning(string exePath);
    /// <summary>Asks the main window to close and waits up to the timeout. True when no instance is left.</summary>
    bool CloseGracefully(string exePath, TimeSpan timeout);
    void ForceKill(string exePath);
}

/// <summary>Questions the engine needs answered by a person (or by quiet mode).</summary>
public interface ISetupPrompts
{
    /// <summary>Remindr is running from the install folder: may setup ask it to close?</summary>
    bool AskCloseRunningApp();
    /// <summary>It did not close in time: may setup force it to quit?</summary>
    bool AskForceClose();
}

/// <summary>Quiet mode: answers yes to everything.</summary>
public sealed class AutoYesPrompts : ISetupPrompts
{
    public bool AskCloseRunningApp() => true;
    public bool AskForceClose() => true;
}

/// <summary>Removes the user's data folder. The stub's real one sends it to the Recycle Bin; tests use a fake.</summary>
public interface IDataRemover
{
    void Remove(string dir);
}

/// <summary>Permanent delete (fallback when no remover is supplied).</summary>
public sealed class PermanentDataRemover : IDataRemover
{
    public void Remove(string dir) => InstallEngine.DeleteDirectory(dir);
}

/// <summary>When the person's data may be removed.</summary>
public static class UninstallPolicy
{
    /// <summary>Interactive: the box must be ticked AND the "Delete everything" question answered yes.</summary>
    public static bool ShouldRemoveData(bool boxTicked, bool confirmedExplicitly) => boxTicked && confirmedExplicitly;

    /// <summary>Quiet: data is kept unless /removedata was passed.</summary>
    public static bool QuietRemoveData(SetupArgs args) => args.RemoveData;
}

public interface ISetupLog
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLog : ISetupLog
{
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { }
}

public sealed class SetupCancelledException : Exception
{
    public SetupCancelledException(string message) : base(message) { }
}

public sealed class SetupProgress
{
    public SetupProgress(string step, double fraction)
    {
        Step = step;
        Fraction = Math.Max(0, Math.Min(1, fraction));
    }
    public string Step { get; }
    /// <summary>0..1 over the whole operation.</summary>
    public double Fraction { get; }
}

/// <summary>Values that identify the app being installed. The stub fills them from RepoInfo and the assembly version.</summary>
public sealed class AppIdentity
{
    public AppIdentity(string version, string publisher, string aumid, string repoUrl, string issuesUrl)
    {
        Version = version;
        Publisher = publisher;
        Aumid = aumid;
        RepoUrl = repoUrl;
        IssuesUrl = issuesUrl;
    }
    public string Version { get; }
    public string Publisher { get; }
    public string Aumid { get; }
    public string RepoUrl { get; }
    public string IssuesUrl { get; }
}
