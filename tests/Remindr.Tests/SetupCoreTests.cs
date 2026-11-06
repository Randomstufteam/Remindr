using System.IO;
using System.IO.Compression;
using Remindr.Setup.Core;

namespace Remindr.Tests;

/// <summary>
/// Tests for installer/Remindr.Setup.Core (netstandard2.0, shared with the net48 setup stub).
/// Nothing here touches the real registry, Start menu or %LocalAppData%\Programs: the registry and shortcuts are fakes
/// and every folder lives under a throwaway temp directory.
/// </summary>
public class SetupCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "remindr-setup-test-" + Guid.NewGuid().ToString("N"));
    private static readonly AppIdentity App = new("0.3.0", RepoInfo.Publisher, RepoInfo.Aumid, RepoInfo.RepoUrl, RepoInfo.IssuesUrl);

    public SetupCoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private InstallPaths Paths() => new(
        Path.Combine(_root, "Local"), Path.Combine(_root, "Roaming"), Path.Combine(_root, "StartMenu"), Path.Combine(_root, "Desktop"));

    // ---- fakes ----
    private sealed class FakeRegistry : IRegistryStore
    {
        public readonly Dictionary<string, Dictionary<string, object>> Keys = new(StringComparer.OrdinalIgnoreCase);
        public bool FailOnUninstallWrite;
        public bool KeyExists(string k) => Keys.ContainsKey(k);
        public IDictionary<string, object>? ReadAll(string k) => Keys.TryGetValue(k, out var d) ? new Dictionary<string, object>(d) : null;
        public string? GetString(string k, string n) => Keys.TryGetValue(k, out var d) && d.TryGetValue(n, out var v) ? v as string : null;
        public void SetValues(string k, IEnumerable<KeyValuePair<string, object>> values)
        {
            if (FailOnUninstallWrite && k == InstallPaths.UninstallSubKey) throw new IOException("boom");
            if (!Keys.TryGetValue(k, out var d)) Keys[k] = d = new Dictionary<string, object>();
            foreach (var kv in values) d[kv.Key] = kv.Value;
        }
        public void DeleteValue(string k, string n) { if (Keys.TryGetValue(k, out var d)) d.Remove(n); }
        public void DeleteKeyTree(string k) => Keys.Remove(k);
    }

    private sealed class FakeShortcuts : IShortcutService
    {
        public readonly List<(string Path, string? Aumid)> Created = new();
        public void Create(string lnk, string target, string? args, string wd, string desc, string? icon, string? aumid)
        {
            File.WriteAllText(lnk, "lnk");
            Created.Add((lnk, aumid));
        }
    }

    private sealed class FakeProcs : IAppProcessGuard
    {
        public bool Running, ClosesGracefully = true;
        public int Kills;
        public bool IsRunning(string exe) => Running;
        public bool CloseGracefully(string exe, TimeSpan t) { if (ClosesGracefully) Running = false; return ClosesGracefully; }
        public void ForceKill(string exe) { Kills++; Running = false; }
    }

    private sealed class Prompts : ISetupPrompts
    {
        public bool Close = true, Force = true;
        public int CloseAsked, ForceAsked;
        public bool AskCloseRunningApp() { CloseAsked++; return Close; }
        public bool AskForceClose() { ForceAsked++; return Force; }
    }

    private static byte[] MakeZip(params (string Name, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (n, c) in files)
                using (var w = new StreamWriter(z.CreateEntry(n).Open())) w.Write(c);
        return ms.ToArray();
    }

    private static readonly byte[] StubBytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

    private (InstallEngine Engine, FakeRegistry Reg, FakeShortcuts Lnk, FakeProcs Procs, InstallPaths Paths) NewEngine(FakeProcs? procs = null)
    {
        var p = Paths();
        var reg = new FakeRegistry();
        var lnk = new FakeShortcuts();
        procs ??= new FakeProcs();
        return (new InstallEngine(p, App, reg, lnk, procs, new NullLog()), reg, lnk, procs, p);
    }

    private static void Install(InstallEngine e, InstallOptions o, byte[] zip, ISetupPrompts? prompts = null, List<SetupProgress>? log = null) =>
        e.Install(o, () => new MemoryStream(zip), () => new MemoryStream(StubBytes), prompts ?? new AutoYesPrompts(), pr => log?.Add(pr));

    // ---- paths / registry values ----
    [Fact]
    public void Paths_are_per_user_and_staging_is_a_sibling()
    {
        var p = new InstallPaths(@"C:\L", @"C:\R", @"C:\S", @"C:\D");
        Assert.Equal(@"C:\L\Programs\Remindr", p.InstallDir);
        Assert.Equal(@"C:\L\Programs\Remindr.new", p.StagingDir);
        Assert.Equal(@"C:\L\Programs\Remindr\Remindr.exe", p.ExePath);
        Assert.Equal(@"C:\R\Remindr", p.DataDir);
        Assert.Equal(@"C:\S\Remindr.lnk", p.StartMenuShortcut);
        Assert.Equal("\"C:\\L\\Programs\\Remindr\\Remindr.exe\" --minimized", p.RunCommand);
    }

    [Fact]
    public void Uninstall_entry_has_every_value_with_repo_info()
    {
        var p = Paths();
        var v = UninstallEntry.Build(p, App, 1234, new DateTime(2026, 10, 24)).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal("Remindr", v["DisplayName"]);
        Assert.Equal("0.3.0", v["DisplayVersion"]);
        Assert.Equal(RepoInfo.Publisher, v["Publisher"]);
        Assert.Equal(p.ExePath, v["DisplayIcon"]);
        Assert.Equal(p.InstallDir, v["InstallLocation"]);
        Assert.Equal("\"" + p.UninstallerPath + "\" /uninstall", v["UninstallString"]);
        Assert.Equal("\"" + p.UninstallerPath + "\" /uninstall /quiet", v["QuietUninstallString"]);
        Assert.Equal(1234, v["EstimatedSize"]);
        Assert.IsType<int>(v["EstimatedSize"]);
        Assert.Equal("20261024", v["InstallDate"]);
        Assert.Equal(RepoInfo.RepoUrl, v["URLInfoAbout"]);
        Assert.Equal(1, v["NoModify"]);
        Assert.Equal(1, v["NoRepair"]);
    }

    // ---- version compare ----
    [Theory]
    [InlineData(null, "0.3.0", InstallMode.Fresh)]
    [InlineData("0.2.0", "0.3.0", InstallMode.Update)]
    [InlineData("0.3.0", "0.3.0", InstallMode.Reinstall)]
    [InlineData("0.3", "0.3.0", InstallMode.Reinstall)]
    [InlineData("0.3.0.0", "0.3.0", InstallMode.Reinstall)]
    [InlineData("0.10.0", "0.9.0", InstallMode.Downgrade)]
    [InlineData("0.3.1", "0.3.0", InstallMode.Downgrade)]
    [InlineData("0.3.0-beta", "0.3.0", InstallMode.Reinstall)]
    [InlineData("garbage", "0.3.0", InstallMode.Update)]
    public void Version_classification(string? installed, string current, InstallMode expected) =>
        Assert.Equal(expected, VersionCompare.Classify(installed, current));

    // ---- args ----
    [Fact]
    public void Args_parse_switch_styles()
    {
        var a = SetupArgs.Parse(new[] { "/uninstall", "--QUIET", "-demo" });
        Assert.True(a.Uninstall); Assert.True(a.Quiet); Assert.True(a.Demo); Assert.False(a.DemoUpdate);
        Assert.True(SetupArgs.Parse(new[] { "--demo-update" }).DemoUpdate);
        Assert.True(SetupArgs.Parse(Array.Empty<string>(), @"C:\x\Uninstall.exe").Uninstall);
        Assert.False(SetupArgs.Parse(Array.Empty<string>(), @"C:\x\Remindr-Installer.exe").Uninstall);
    }

    // ---- payload ----
    [Fact]
    public void Payload_footer_round_trips_and_locates_zip()
    {
        var zip = MakeZip(("Remindr.exe", "hello"));
        using var ms = new MemoryStream();
        ms.Write(StubBytes); ms.Write(zip); ms.Write(PayloadInfo.BuildFooter(StubBytes.Length));
        ms.Position = 0;

        var info = PayloadInfo.TryRead(ms);
        Assert.NotNull(info);
        Assert.Equal(StubBytes.Length, info!.StubLength);
        Assert.Equal(zip.Length, info.ZipLength);

        using var sub = new SubStream(ms, info.StubLength, info.ZipLength);
        using var archive = new ZipArchive(sub, ZipArchiveMode.Read);
        Assert.Equal("Remindr.exe", archive.Entries.Single().FullName);
    }

    [Fact]
    public void Payload_footer_missing_or_wrong_returns_null()
    {
        Assert.Null(PayloadInfo.TryRead(new MemoryStream(StubBytes)));
        Assert.Null(PayloadInfo.TryRead(new MemoryStream(new byte[3])));
        var bad = new byte[40];
        PayloadInfo.BuildFooter(99999).CopyTo(bad, 24);   // offset beyond the file
        Assert.Null(PayloadInfo.TryRead(new MemoryStream(bad)));
    }

    [Fact]
    public void Extract_refuses_path_traversal()
    {
        using var ms = new MemoryStream(MakeZip(("..\\evil.txt", "x")));
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => ZipExtractor.ExtractAll(zip, Path.Combine(_root, "out")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    // ---- install engine ----
    [Fact]
    public void Fresh_install_writes_files_shortcuts_and_registry()
    {
        var (e, reg, lnk, _, p) = NewEngine();
        var steps = new List<SetupProgress>();
        Install(e, new InstallOptions { DesktopShortcut = true, StartWithWindows = true },
            MakeZip(("Remindr.exe", "EXE"), ("LICENSE", "MIT")), log: steps);

        Assert.Equal("EXE", File.ReadAllText(p.ExePath));
        Assert.Equal(StubBytes, File.ReadAllBytes(p.UninstallerPath));
        Assert.False(Directory.Exists(p.StagingDir));
        Assert.False(Directory.Exists(p.BackupDir));
        Assert.Contains(lnk.Created, c => c.Path == p.StartMenuShortcut && c.Aumid == RepoInfo.Aumid);
        Assert.Contains(lnk.Created, c => c.Path == p.DesktopShortcut);
        // Start with Windows is on: the Run value is the quoted exe plus --minimized (the format the app's Settings toggle writes too).
        Assert.True(SetupFeatures.StartWithWindows);
        Assert.Equal("\"" + p.ExePath + "\" --minimized", reg.GetString(InstallPaths.RunSubKey, "Remindr"));
        Assert.Equal("0.3.0", reg.GetString(InstallPaths.UninstallSubKey, "DisplayVersion"));
        Assert.Equal((3 + 3 + 5000 + 1023) / 1024, reg.Keys[InstallPaths.UninstallSubKey]["EstimatedSize"]);
        Assert.Equal(1.0, steps.Last().Fraction);
        Assert.Contains(steps, s => s.Step == "Extracting files");
        Assert.Contains(steps, s => s.Step == "Creating shortcuts");
        Assert.Contains(steps, s => s.Step == "Registering app");
        Assert.False(Directory.Exists(p.DataDir));   // data folder never created or touched
    }

    [Fact]
    public void Optional_items_are_off_when_not_ticked()
    {
        var (e, reg, lnk, _, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "EXE")));
        Assert.Null(reg.GetString(InstallPaths.RunSubKey, "Remindr"));
        Assert.DoesNotContain(lnk.Created, c => c.Path == p.DesktopShortcut);
        Assert.Single(lnk.Created);
    }

    [Fact]
    public void Update_rewrites_the_run_value_when_ticked_and_removes_it_when_not()
    {
        var (e, reg, _, _, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "OLD")));
        reg.Keys[InstallPaths.UninstallSubKey]["DisplayVersion"] = "0.2.0";
        reg.SetValues(InstallPaths.RunSubKey, new[] { new KeyValuePair<string, object>("Remindr", "\"old path\" --minimized") });
        Assert.True(e.Detect().HasRunValue);

        Install(e, new InstallOptions { StartWithWindows = true }, MakeZip(("Remindr.exe", "NEW")));
        Assert.Equal(p.RunCommand, reg.GetString(InstallPaths.RunSubKey, "Remindr"));

        reg.Keys[InstallPaths.UninstallSubKey]["DisplayVersion"] = "0.2.0";
        Install(e, new InstallOptions { StartWithWindows = false }, MakeZip(("Remindr.exe", "NEWER")));
        Assert.Null(reg.GetString(InstallPaths.RunSubKey, "Remindr"));
    }

    [Fact]
    public void Update_replaces_files_and_keeps_data()
    {
        var (e, reg, _, _, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "OLD"), ("old-only.txt", "x")));
        Directory.CreateDirectory(p.DataDir);
        File.WriteAllText(Path.Combine(p.DataDir, "remindr.db"), "precious");
        reg.Keys[InstallPaths.UninstallSubKey]["DisplayVersion"] = "0.2.0";
        Assert.Equal(InstallMode.Update, e.Detect().Mode);

        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "NEW")));

        Assert.Equal("NEW", File.ReadAllText(p.ExePath));
        Assert.False(File.Exists(Path.Combine(p.InstallDir, "old-only.txt")));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(p.DataDir, "remindr.db")));
        Assert.Equal("0.3.0", reg.GetString(InstallPaths.UninstallSubKey, "DisplayVersion"));
        Assert.Equal(InstallMode.Reinstall, e.Detect().Mode);
    }

    [Fact]
    public void Failed_registry_write_rolls_back_to_the_old_version()
    {
        var (e, reg, _, _, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "OLD")));
        reg.Keys[InstallPaths.UninstallSubKey]["DisplayVersion"] = "0.2.0";

        reg.FailOnUninstallWrite = true;
        Assert.Throws<IOException>(() => Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "NEW"))));
        reg.FailOnUninstallWrite = false;

        Assert.Equal("OLD", File.ReadAllText(p.ExePath));
        Assert.False(Directory.Exists(p.StagingDir));
        Assert.False(Directory.Exists(p.BackupDir));
        Assert.Equal("0.2.0", reg.GetString(InstallPaths.UninstallSubKey, "DisplayVersion"));
    }

    [Fact]
    public void Failed_fresh_install_leaves_nothing_behind()
    {
        var (e, reg, _, _, p) = NewEngine();
        reg.FailOnUninstallWrite = true;
        Assert.Throws<IOException>(() => Install(e, new InstallOptions { DesktopShortcut = true }, MakeZip(("Remindr.exe", "EXE"))));

        Assert.False(Directory.Exists(p.InstallDir));
        Assert.False(File.Exists(p.StartMenuShortcut));
        Assert.False(File.Exists(p.DesktopShortcut));
        Assert.False(reg.KeyExists(InstallPaths.UninstallSubKey));
    }

    [Fact]
    public void Payload_without_the_exe_fails_before_touching_the_install()
    {
        var (e, _, _, _, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "OLD")));
        Assert.Throws<InvalidDataException>(() => Install(e, new InstallOptions(), MakeZip(("readme.txt", "x"))));
        Assert.Equal("OLD", File.ReadAllText(p.ExePath));
    }

    [Fact]
    public void Running_app_is_asked_closed_then_forced_only_with_ok()
    {
        var procs = new FakeProcs { Running = true, ClosesGracefully = false };
        var (e, _, _, _, p) = NewEngine(procs);
        var prompts = new Prompts();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "EXE")), prompts);
        Assert.Equal(1, prompts.CloseAsked); Assert.Equal(1, prompts.ForceAsked); Assert.Equal(1, procs.Kills);
        Assert.True(File.Exists(p.ExePath));

        // user refuses to force close: setup is cancelled, nothing changes
        procs.Running = true; procs.Kills = 0;
        Assert.Throws<SetupCancelledException>(() =>
            Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "NEW")), new Prompts { Force = false }));
        Assert.Equal(0, procs.Kills);
        Assert.Equal("EXE", File.ReadAllText(p.ExePath));
    }

    [Fact]
    public void Graceful_close_never_asks_to_force()
    {
        var procs = new FakeProcs { Running = true, ClosesGracefully = true };
        var (e, _, _, _, _) = NewEngine(procs);
        var prompts = new Prompts();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "EXE")), prompts);
        Assert.Equal(0, prompts.ForceAsked);
        Assert.Equal(0, procs.Kills);
    }

    // ---- uninstall engine ----
    private (UninstallEngine U, FakeRegistry Reg, InstallPaths P) InstalledSystem(bool desktop = true)
    {
        var (e, reg, _, procs, p) = NewEngine();
        Install(e, new InstallOptions { DesktopShortcut = desktop }, MakeZip(("Remindr.exe", "EXE")));
        // Seed a Run value as an older install would have left it; uninstall must still remove it.
        reg.SetValues(InstallPaths.RunSubKey, new[] { new KeyValuePair<string, object>("Remindr", p.RunCommand) });
        reg.SetValues(InstallPaths.AumidSubKey(RepoInfo.Aumid), new[] { new KeyValuePair<string, object>("DisplayName", "Remindr") });
        reg.SetValues(InstallPaths.ToastClsidSubKey, new[] { new KeyValuePair<string, object>("x", "y") });
        Directory.CreateDirectory(p.ToastCompatDir);
        File.WriteAllText(Path.Combine(p.ToastCompatDir, "DisplayName.txt"), "x");
        Directory.CreateDirectory(p.DataDir);
        File.WriteAllText(Path.Combine(p.DataDir, "remindr.db"), "precious");
        return (new UninstallEngine(p, App, reg, procs, new NullLog()), reg, p);
    }

    [Fact]
    public void Uninstall_removes_everything_but_keeps_data_by_default()
    {
        var (u, reg, p) = InstalledSystem();
        u.Uninstall(false, new AutoYesPrompts(), _ => { });

        Assert.False(Directory.Exists(p.InstallDir));
        Assert.False(File.Exists(p.StartMenuShortcut));
        Assert.False(File.Exists(p.DesktopShortcut));
        Assert.False(reg.KeyExists(InstallPaths.UninstallSubKey));
        Assert.False(reg.KeyExists(InstallPaths.AumidSubKey(RepoInfo.Aumid)));
        Assert.Null(reg.GetString(InstallPaths.RunSubKey, "Remindr"));
        Assert.False(reg.KeyExists(InstallPaths.ToastClsidSubKey));
        Assert.False(Directory.Exists(p.ToastCompatDir));
        Assert.True(File.Exists(Path.Combine(p.DataDir, "remindr.db")));
    }

    [Fact]
    public void Uninstall_with_remove_data_deletes_the_data_folder()
    {
        var (u, _, p) = InstalledSystem();
        u.Uninstall(true, new AutoYesPrompts(), _ => { });
        Assert.False(Directory.Exists(p.DataDir));
    }

    [Fact]
    public void Uninstall_removes_the_AI_models_folder_only_with_the_data()
    {
        var (u, _, p) = InstalledSystem();
        Directory.CreateDirectory(p.ModelsDir);
        File.WriteAllText(Path.Combine(p.ModelsDir, "model.gguf"), "big");

        u.Uninstall(false, new AutoYesPrompts(), _ => { });
        Assert.True(File.Exists(Path.Combine(p.ModelsDir, "model.gguf")));

        u.Uninstall(true, new AutoYesPrompts(), _ => { });
        Assert.False(Directory.Exists(p.ModelsDir));
        Assert.EndsWith(Path.Combine("Remindr", "models"), p.ModelsDir);
    }

    private sealed class FakeRemover : IDataRemover
    {
        public List<string> Removed = new();
        public void Remove(string dir) { Removed.Add(dir); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Uninstall_sends_data_through_the_data_remover_only_when_asked()
    {
        var (e, reg, _, procs, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "EXE")));
        Directory.CreateDirectory(p.DataDir);
        File.WriteAllText(Path.Combine(p.DataDir, "remindr.db"), "precious");
        var rem = new FakeRemover();
        var u = new UninstallEngine(p, App, reg, procs, new NullLog(), rem);

        u.Uninstall(false, new AutoYesPrompts(), _ => { });
        Assert.Empty(rem.Removed);
        Assert.True(Directory.Exists(p.DataDir));

        u.Uninstall(true, new AutoYesPrompts(), _ => { });
        Assert.Equal(new[] { p.DataDir }, rem.Removed);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]   // ticked but never confirmed: keep the data
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Remove_data_needs_the_box_and_an_explicit_confirmation(bool ticked, bool confirmed, bool expected) =>
        Assert.Equal(expected, UninstallPolicy.ShouldRemoveData(ticked, confirmed));

    [Fact]
    public void Quiet_uninstall_keeps_data_unless_removedata_is_passed()
    {
        Assert.False(UninstallPolicy.QuietRemoveData(SetupArgs.Parse(new[] { "/uninstall", "/quiet" })));
        Assert.True(UninstallPolicy.QuietRemoveData(SetupArgs.Parse(new[] { "/uninstall", "/quiet", "/removedata" })));
    }

    [Fact]
    public void Uninstall_skips_its_own_exe_when_run_from_the_install_folder()
    {
        var (u, _, p) = InstalledSystem();
        u.Uninstall(false, new AutoYesPrompts(), _ => { }, selfPath: p.UninstallerPath);
        Assert.True(File.Exists(p.UninstallerPath));
        Assert.False(File.Exists(p.ExePath));
    }

    [Fact]
    public void Uninstall_cancelled_when_user_will_not_close_the_app()
    {
        var (e, reg, _, procs, p) = NewEngine();
        Install(e, new InstallOptions(), MakeZip(("Remindr.exe", "EXE")));
        procs.Running = true;
        var u = new UninstallEngine(p, App, reg, procs, new NullLog());
        Assert.Throws<SetupCancelledException>(() => u.Uninstall(false, new Prompts { Close = false }, _ => { }));
        Assert.True(File.Exists(p.ExePath));
        Assert.True(reg.KeyExists(InstallPaths.UninstallSubKey));
    }
}
