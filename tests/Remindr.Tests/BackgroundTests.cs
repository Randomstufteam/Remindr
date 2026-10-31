using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;

namespace Remindr.Tests;

/// <summary>Phase 4.5: launch arguments, single instance (rules and a real pipe round trip) and the start-with-Windows value.</summary>
public class BackgroundTests
{
    // ---- arguments ----
    [Fact]
    public void Parse_FindsKnownSwitches_AnyCase()
    {
        var a = LaunchArgs.Parse(new[] { "--MINIMIZED", "-toastactivated", "--welcome" });
        Assert.True(a.Minimized); Assert.True(a.ToastActivated); Assert.True(a.Welcome);
    }

    [Fact]
    public void Parse_IgnoresUnknownAndMissingArgs()
    {
        Assert.Equal(new LaunchArgs(false, false, false), LaunchArgs.Parse(null));
        Assert.Equal(new LaunchArgs(false, false, false), LaunchArgs.Parse(new[] { "--future", "C:\\x.txt", "-ToastActivated2", "minimized" }));
        Assert.Equal(new LaunchArgs(true, false, false), LaunchArgs.Parse(new[] { "--future", "--minimized" }));
    }

    // ---- single-instance rules ----
    [Fact]
    public void SecondLaunch_BringsForward_UnlessMinimized()
    {
        Assert.True(SingleInstanceRules.ShouldBringForward(LaunchArgs.Parse(Array.Empty<string>())));
        Assert.False(SingleInstanceRules.ShouldBringForward(LaunchArgs.Parse(new[] { "--minimized" })));
    }

    [Fact]
    public void DuplicateToastLaunch_ForwardsNothing_OthersForward()
    {
        Assert.Equal(DuplicateLaunchAction.ExitSilently, SingleInstanceRules.ForDuplicate(LaunchArgs.Parse(new[] { "-ToastActivated" })));
        Assert.Equal(DuplicateLaunchAction.ForwardAndExit, SingleInstanceRules.ForDuplicate(LaunchArgs.Parse(Array.Empty<string>())));
        Assert.Equal(DuplicateLaunchAction.ForwardAndExit, SingleInstanceRules.ForDuplicate(LaunchArgs.Parse(new[] { "--minimized" })));
    }

    [Fact]
    public void Message_RoundTrips_AndRejectsGarbage()
    {
        var text = SingleInstanceRules.Encode(new[] { "--minimized", "a \"quoted\"\nline" });
        Assert.DoesNotContain('\n', text);
        Assert.Equal(new[] { "--minimized", "a \"quoted\"\nline" }, SingleInstanceRules.Decode(text));
        Assert.Empty(SingleInstanceRules.Decode(SingleInstanceRules.Encode(null))!);

        Assert.Null(SingleInstanceRules.Decode(null));
        Assert.Null(SingleInstanceRules.Decode(""));
        Assert.Null(SingleInstanceRules.Decode("not json"));
        Assert.Null(SingleInstanceRules.Decode("{\"v\":2,\"args\":[]}"));
        Assert.Null(SingleInstanceRules.Decode("{\"v\":1,\"args\":[null]}"));
        Assert.Null(SingleInstanceRules.Decode(new string('x', SingleInstanceRules.MaxMessageBytes + 1)));
    }

    // ---- real mutex and pipe, with a unique name per test ----
    private static string NewId() => "test." + Guid.NewGuid().ToString("N");

    /// <summary>Timeouts for pipe tests that are about content, not timing.</summary>
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(30);

    [Fact]
    public void OnlyTheFirstProcessBecomesPrimary()
    {
        var id = NewId();
        using var first = new SingleInstance(id);
        using var second = new SingleInstance(id);
        Assert.True(first.TryBecomePrimary());
        Assert.False(second.TryBecomePrimary());
    }

    /// <summary>Holds the mutex on its own thread (a mutex is re-entrant on one thread) until <paramref name="release"/> is set.</summary>
    private static Thread HoldOnOtherThread(string id, ManualResetEventSlim held, ManualResetEventSlim release, bool releaseProperly)
    {
        var t = new Thread(() =>
        {
            var si = new SingleInstance(id);
            Assert.True(si.TryBecomePrimary());
            held.Set();
            release.Wait();
            // Ending the thread without ReleaseMutex abandons it, like a crashed or exiting process.
            if (!releaseProperly) return;
            si.Dispose();
        }) { IsBackground = true };
        t.Start();
        held.Wait();
        return t;
    }

    [Fact]
    public void WaitToBecomePrimary_TakesOver_WhenTheOwnerGoesAway()
    {
        var id = NewId();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = HoldOnOtherThread(id, held, release, releaseProperly: false);
        using var second = new SingleInstance(id);
        Assert.False(second.TryBecomePrimary());
        release.Set();
        owner.Join();
        Assert.True(second.WaitToBecomePrimary(TimeSpan.FromSeconds(5)));   // abandoned counts as acquired
    }

    [Fact]
    public void WaitToBecomePrimary_GivesUp_WhileTheOwnerStays()
    {
        var id = NewId();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = HoldOnOtherThread(id, held, release, releaseProperly: true);
        try
        {
            using var second = new SingleInstance(id);
            Assert.False(second.TryBecomePrimary());
            Assert.False(second.WaitToBecomePrimary(TimeSpan.FromMilliseconds(200)));
        }
        finally { release.Set(); owner.Join(); }
    }

    [Fact]
    public async Task Pipe_DeliversArgs_AndWaitsForTheHandler()
    {
        // Not a timing test: long timeouts on both sides, so a busy machine (the whole suite runs in parallel) cannot fail it.
        var id = NewId();
        using var si = new SingleInstance(id, Patient);
        string[]? got = null;
        si.StartServer(async args => { await Task.Delay(50); got = args; });
        using var client = new SingleInstance(id);

        Assert.True(await client.SendToRunningAsync(new[] { "--minimized" }, Patient));
        Assert.Equal(new[] { "--minimized" }, got);   // the "ok" only comes after the handler finished
    }

    [Fact]
    public async Task Send_WithNobodyListening_ReturnsFalseQuickly()
    {
        using var client = new SingleInstance(NewId());
        var started = DateTime.UtcNow;
        Assert.False(await client.SendToRunningAsync(new[] { "x" }, TimeSpan.FromMilliseconds(400)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Send_ToAFrozenInstance_GivesUpWithoutHanging()
    {
        var id = NewId();
        using var si = new SingleInstance(id);
        si.StartServer(_ => new TaskCompletionSource().Task);   // a UI thread that never answers
        using var client = new SingleInstance(id);
        var started = DateTime.UtcNow;
        Assert.False(await client.SendToRunningAsync(new[] { "x" }, TimeSpan.FromMilliseconds(600)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Pipe_ServerSurvivesGarbage()
    {
        var id = NewId();
        using var si = new SingleInstance(id, Patient);
        var count = 0;
        si.StartServer(_ => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        using (var raw = new System.IO.Pipes.NamedPipeClientStream(".", "Remindr.App." + id, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            raw.Connect((int)Patient.TotalMilliseconds);
            raw.Write(System.Text.Encoding.UTF8.GetBytes("garbage\n"));
        }
        using var client = new SingleInstance(id);
        Assert.True(await client.SendToRunningAsync(new[] { "a" }, Patient));
        Assert.Equal(1, count);
    }

    // ---- start with Windows ----
    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Run = new();
        public Dictionary<string, byte[]> Approved = new();
        public bool Throw;
        public string? GetRun(string name) { Check(); return Run.GetValueOrDefault(name); }
        public void SetRun(string name, string command) { Check(); Run[name] = command; }
        public void DeleteRun(string name) { Check(); Run.Remove(name); }
        public byte[]? GetApproved(string name) { Check(); return Approved.GetValueOrDefault(name); }
        public void DeleteApproved(string name) { Check(); Approved.Remove(name); }
        private void Check() { if (Throw) throw new UnauthorizedAccessException(); }
    }

    private static (TempDb db, StartWithWindowsService svc, FakeStartupRegistry reg, SettingsRepo settings) NewStartup(string? exe = @"C:\Apps\Remindr\Remindr.exe")
    {
        var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var settings = new SettingsRepo(t.Db);
        var reg = new FakeStartupRegistry();
        return (t, new StartWithWindowsService(reg, settings, () => exe), reg, settings);
    }

    [Fact]
    public void Startup_On_WritesQuotedExeWithMinimized_AndMirrorsTheSetting()
    {
        var (t, svc, reg, settings) = NewStartup();
        using (t)
        {
            Assert.False(svc.Read());
            Assert.Equal("0", settings.Get("start_with_windows"));
            Assert.True(svc.Set(true));
            Assert.Equal("\"C:\\Apps\\Remindr\\Remindr.exe\" --minimized", reg.Run["Remindr"]);
            Assert.Equal("1", settings.Get("start_with_windows"));
        }
    }

    [Fact]
    public void Startup_Off_DeletesTheValue()
    {
        var (t, svc, reg, settings) = NewStartup();
        using (t)
        {
            svc.Set(true);
            Assert.False(svc.Set(false));
            Assert.False(reg.Run.ContainsKey("Remindr"));
            Assert.Equal("0", settings.Get("start_with_windows"));
        }
    }

    [Fact]
    public void Startup_ReadsTheRealState_NotTheSetting()
    {
        var (t, svc, reg, settings) = NewStartup();
        using (t)
        {
            settings.Set("start_with_windows", "1");          // stale mirror
            Assert.False(svc.Read());
            Assert.Equal("0", settings.Get("start_with_windows"));
            reg.Run["Remindr"] = "\"x\" --minimized";           // set from outside (installer)
            Assert.True(svc.Read());
            Assert.Equal("1", settings.Get("start_with_windows"));
        }
    }

    [Fact]
    public void Startup_DisabledInTaskManager_ShowsOff_AndTurningOnReEnables()
    {
        var (t, svc, reg, _) = NewStartup();
        using (t)
        {
            reg.Run["Remindr"] = "\"x\" --minimized";
            reg.Approved["Remindr"] = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            Assert.False(svc.Read());
            Assert.True(svc.Set(true));
            Assert.False(reg.Approved.ContainsKey("Remindr"));
            reg.Approved["Remindr"] = new byte[] { 2, 0, 0, 0 };   // enabled marker
            Assert.True(svc.Read());
        }
    }

    [Fact]
    public void Startup_RegistryFailure_DoesNotThrow_AndReportsOff()
    {
        var (t, svc, reg, _) = NewStartup();
        using (t)
        {
            reg.Throw = true;
            Assert.False(svc.Set(true));
            Assert.False(svc.Read());
        }
    }

    [Fact]
    public void Startup_CommandFormat_MatchesTheInstaller()
    {
        var paths = new Remindr.Setup.Core.InstallPaths(@"C:\L", @"C:\A", @"C:\S", @"C:\D");
        Assert.Equal(paths.RunCommand, StartWithWindowsService.CommandFor(paths.ExePath));
    }
}
