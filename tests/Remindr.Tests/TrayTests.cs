using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;

namespace Remindr.Tests;

public class TrayTests
{
    private static (TempDb db, TraySettings tray, SettingsRepo repo) Create()
    {
        var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var repo = new SettingsRepo(t.Db);
        return (t, new TraySettings(repo), repo);
    }

    [Fact]
    public void CloseToTray_DefaultsOn()
    {
        var (t, tray, _) = Create();
        using (t) Assert.True(tray.CloseToTray);
    }

    [Fact]
    public void CloseToTray_PersistsAcrossInstances()
    {
        var (t, tray, repo) = Create();
        using (t)
        {
            tray.CloseToTray = false;
            Assert.False(new TraySettings(repo).CloseToTray);
            Assert.Equal("0", repo.Get("close_to_tray"));
            tray.CloseToTray = true;
            Assert.True(new TraySettings(repo).CloseToTray);
        }
    }

    [Fact]
    public void SettingOn_HidesAndShowsNoticeOnlyTheFirstTime()
    {
        var (t, tray, repo) = Create();
        using (t)
        {
            Assert.Equal(new CloseDecision(true, true), tray.OnCloseRequested(false));
            Assert.Equal(new CloseDecision(true, false), tray.OnCloseRequested(false));
            // Remembered across restarts.
            Assert.Equal(new CloseDecision(true, false), new TraySettings(repo).OnCloseRequested(false));
        }
    }

    [Fact]
    public void SettingOff_ClosesWithoutNoticeAndKeepsNoticeUnused()
    {
        var (t, tray, _) = Create();
        using (t)
        {
            tray.CloseToTray = false;
            Assert.Equal(new CloseDecision(false, false), tray.OnCloseRequested(false));
            tray.CloseToTray = true;
            Assert.Equal(new CloseDecision(true, true), tray.OnCloseRequested(false));
        }
    }

    [Fact]
    public void Exit_BypassesTrayEvenWhenOn_AndDoesNotConsumeNotice()
    {
        var (t, tray, _) = Create();
        using (t)
        {
            Assert.Equal(new CloseDecision(false, false), tray.OnCloseRequested(true));
            Assert.Equal(new CloseDecision(true, true), tray.OnCloseRequested(false));
        }
    }

    [Fact]
    public void QuitSignal_FiresWhenTheInstallerSetsTheNamedEvent()
    {
        using var signal = new QuitSignal();
        using var fired = new ManualResetEventSlim();
        signal.Start(fired.Set);
        // What the installer's ProcessGuard does for each running Remindr.
        Assert.True(EventWaitHandle.TryOpenExisting(QuitSignal.NameFor(Environment.ProcessId), out var quit));
        using (quit) quit.Set();
        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)));
    }
}
