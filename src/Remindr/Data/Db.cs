using System.IO;
using Microsoft.Data.Sqlite;

namespace Remindr.Data;

/// <summary>Connection factory. The database path is injectable so tests can use a temp file.</summary>
public sealed class Db
{
    static Db() => TypeHandlers.Register();

    /// <summary>%AppData%\Remindr\remindr.db, or remindr.db inside the REMINDR_DATA_DIR folder when that is set.</summary>
    public static string DefaultPath { get; } = Remindr.Services.AppPaths.Current.DbFile;

    public string FilePath { get; }

    public Db() : this(DefaultPath) { }

    public Db(string path) => FilePath = path;

    public SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }
}



