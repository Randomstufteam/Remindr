using System;
using System.Threading;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>What the UI flows call. <see cref="RealBackend"/> does the work; <see cref="DemoBackend"/> pretends and writes nothing.</summary>
public interface ISetupBackend
{
    bool IsDemo { get; }
    ExistingInstall Detect();
    /// <summary>Blocking; the flow runs it on a worker thread.</summary>
    void Install(InstallOptions options, ISetupPrompts prompts, Action<SetupProgress> progress);
    void Uninstall(bool removeData, ISetupPrompts prompts, Action<SetupProgress> progress);
    /// <summary>Starts the installed Remindr. Never throws.</summary>
    void LaunchApp();
    string InstallDir { get; }
    string DataDir { get; }
}

/// <summary>Fake run for screenshots and UI testing. Touches no registry, folder, shortcut or process.</summary>
public sealed class DemoBackend : ISetupBackend
{
    private readonly bool _update;

    public DemoBackend(bool pretendUpdate) { _update = pretendUpdate; }

    public bool IsDemo => true;
    public string InstallDir => @"C:\Users\You\AppData\Local\Programs\Remindr";
    public string DataDir => @"C:\Users\You\AppData\Roaming\Remindr";

    public ExistingInstall Detect() => _update
        ? new ExistingInstall { Mode = InstallMode.Update, InstalledVersion = "0.2.0", HasDesktopShortcut = true, HasRunValue = false }
        : new ExistingInstall { Mode = InstallMode.Fresh };

    public void Install(InstallOptions options, ISetupPrompts prompts, Action<SetupProgress> progress)
    {
        Fake(progress, new[] { ("Extracting files", 0.0, 0.7), ("Creating shortcuts", 0.7, 0.85), ("Registering app", 0.85, 1.0) });
    }

    public void Uninstall(bool removeData, ISetupPrompts prompts, Action<SetupProgress> progress)
    {
        Fake(progress, new[] { ("Removing files", 0.0, 0.6), ("Removing shortcuts", 0.6, 0.8), ("Cleaning up settings", 0.8, 1.0) });
    }

    public void LaunchApp() { }

    private static void Fake(Action<SetupProgress> progress, (string Step, double From, double To)[] steps)
    {
        foreach (var (step, from, to) in steps)
        {
            const int ticks = 12;
            for (var i = 0; i <= ticks; i++)
            {
                progress(new SetupProgress(step, from + (to - from) * i / ticks));
                Thread.Sleep(90);
            }
        }
    }
}
