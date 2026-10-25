using Dapper;
using Microsoft.Data.Sqlite;
using Remindr.Models;

namespace Remindr.Data.Repositories;

public sealed class CategoryRepo
{
    public static readonly (string Name, string Color)[] Defaults =
    {
        ("School", "#3B82F6"), ("Work", "#F59E0B"), ("Personal", "#10B981")
    };

    private readonly Db _db;

    public CategoryRepo(Db db) => _db = db;

    public IReadOnlyList<Category> ListByProfile(int profileId)
    {
        using var conn = _db.Open();
        return conn.Query<Category>(
            @"SELECT id AS Id, profile_id AS ProfileId, name AS Name, color AS Color, sort_order AS SortOrder
              FROM categories WHERE profile_id=@profileId ORDER BY sort_order, id",
            new { profileId }).ToList();
    }

    /// <summary>
    /// Seeds School/Work/Personal (the colours are fixed; <paramref name="names"/> optionally gives the three names in another language,
    /// in the same order). Used inside the profile-create transaction.
    /// </summary>
    public static void SeedDefaults(SqliteConnection conn, SqliteTransaction? tx, int profileId, IReadOnlyList<string>? names = null)
    {
        for (var i = 0; i < Defaults.Length; i++)
            conn.Execute(
                "INSERT INTO categories(profile_id, name, color, sort_order) VALUES (@profileId, @name, @color, @i)",
                new { profileId, name = names is not null && i < names.Count ? names[i] : Defaults[i].Name, color = Defaults[i].Color, i }, tx);
    }
}
