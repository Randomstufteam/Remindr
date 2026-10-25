using Dapper;

namespace Remindr.Data.Repositories;

public sealed class SettingsRepo
{
    private readonly Db _db;

    public SettingsRepo(Db db) => _db = db;

    /// <summary>profileId 0 = global.</summary>
    public string? Get(string key, int profileId = 0)
    {
        using var conn = _db.Open();
        return conn.QuerySingleOrDefault<string>(
            "SELECT value FROM app_settings WHERE profile_id=@profileId AND key=@key",
            new { profileId, key });
    }

    public void Set(string key, string value, int profileId = 0)
    {
        using var conn = _db.Open();
        conn.Execute(
            @"INSERT INTO app_settings(profile_id, key, value) VALUES (@profileId, @key, @value)
              ON CONFLICT(profile_id, key) DO UPDATE SET value=excluded.value",
            new { profileId, key, value });
    }
}
