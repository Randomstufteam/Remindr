using System.IO;
using Dapper;
using Remindr.Data;
using Remindr.Data.Repositories;

namespace Remindr.Tests;

public sealed class TempDb : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "remindr-tests-" + Guid.NewGuid().ToString("N"));
    public Db Db { get; }

    public TempDb() => Db = new Db(Path.Combine(Dir, "test.db"));

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public class DbInitializerTests
{
    private static readonly string[] Tables =
        { "schema_version", "profiles", "categories", "reminders", "notification_log", "app_settings" };

    [Fact]
    public void Initialize_CreatesAllTablesAndLatestVersion()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();

        using var conn = t.Db.Open();
        var names = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToHashSet();
        foreach (var table in Tables) Assert.Contains(table, names);
        Assert.Equal(Migrations.Latest, conn.ExecuteScalar<int>("SELECT version FROM schema_version"));
        Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM schema_version"));
        Assert.Equal(1, conn.ExecuteScalar<int>("PRAGMA foreign_keys"));
    }

    [Fact]
    public void Initialize_RunTwice_IsNoOp()
    {
        using var t = new TempDb();
        var init = new DbInitializer(t.Db);
        init.Initialize();
        init.Initialize();

        using var conn = t.Db.Open();
        Assert.Equal(Migrations.Latest, conn.ExecuteScalar<int>("SELECT version FROM schema_version"));
        Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM schema_version"));
    }

    [Fact]
    public void Initialize_NewerDatabase_Throws()
    {
        using var t = new TempDb();
        var init = new DbInitializer(t.Db);
        init.Initialize();
        using (var conn = t.Db.Open())
            conn.Execute("UPDATE schema_version SET version = 999");
        Assert.Throws<InvalidOperationException>(() => init.Initialize());
    }
}

public class SettingsRepoTests
{
    [Fact]
    public void SetThenGet_RoundTripsAndOverwrites()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var repo = new SettingsRepo(t.Db);

        Assert.Null(repo.Get("theme"));
        repo.Set("theme", "Dark");
        Assert.Equal("Dark", repo.Get("theme"));
        repo.Set("theme", "Light");
        Assert.Equal("Light", repo.Get("theme"));
    }

    [Fact]
    public void ProfileScopedValues_AreSeparateFromGlobal()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var repo = new SettingsRepo(t.Db);

        repo.Set("k", "global");
        repo.Set("k", "p1", 1);
        Assert.Equal("global", repo.Get("k"));
        Assert.Equal("p1", repo.Get("k", 1));
    }
}


