using System.IO;
using Dapper;

namespace Remindr.Data;

public sealed class DbInitializer
{
    private readonly Db _db;

    public DbInitializer(Db db) => _db = db;

    /// <summary>Creates the folder/file and runs pending migrations. Throws if the DB is newer than the app.</summary>
    public void Initialize()
    {
        var dir = Path.GetDirectoryName(_db.FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = _db.Open();
        var current = GetVersion(conn);

        if (current > Migrations.Latest)
            throw new InvalidOperationException(
                $"The database (schema version {current}) was created by a newer version of Remindr (this build supports up to {Migrations.Latest}).");

        foreach (var (version, sql) in Migrations.All.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var tx = conn.BeginTransaction();
            conn.Execute(sql, transaction: tx);
            conn.Execute("DELETE FROM schema_version", transaction: tx);
            conn.Execute("INSERT INTO schema_version(version) VALUES (@version)", new { version }, tx);
            tx.Commit();
        }
    }

    private static int GetVersion(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        var exists = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'");
        if (exists == 0) return 0;
        return conn.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 0;
    }
}


