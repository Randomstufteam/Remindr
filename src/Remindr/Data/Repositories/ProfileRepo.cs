using Dapper;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.Data.Repositories;

/// <summary>First-run setup tried to create the first profile, but a profile already exists (e.g. a second instance finished setup first).</summary>
public sealed class FirstRunAlreadyCompletedException : InvalidOperationException
{
    public FirstRunAlreadyCompletedException() : base("First-run setup was already completed: a profile already exists.") { }
}

public sealed class ProfileRepo
{
    public const string DefaultProfileName = "Me";
    public const string DefaultProfileColor = "#3B82F6";

    private const string Select =
        @"SELECT id AS Id, name AS Name, color AS Color, avatar_initial AS AvatarInitial,
                 pin_hash AS PinHash, pin_salt AS PinSalt, avatar AS Avatar, created_at AS CreatedAt FROM profiles";

    private readonly Db _db;
    private readonly string _avatarDir;

    /// <param name="avatarDir">Where uploaded pictures live (tests pass a temp folder). Null = %AppData%\Remindr\avatars.</param>
    public ProfileRepo(Db db, string? avatarDir = null)
    {
        _db = db;
        _avatarDir = avatarDir ?? AvatarService.DefaultDirectory;
    }

    public IReadOnlyList<Profile> List()
    {
        using var conn = _db.Open();
        return conn.Query<Profile>(Select + " ORDER BY id").ToList();
    }

    public Profile? Get(int id)
    {
        using var conn = _db.Open();
        return conn.QuerySingleOrDefault<Profile>(Select + " WHERE id=@id", new { id });
    }

    public int Count()
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM profiles");
    }

    /// <summary>Creates the profile, seeds the default categories, optionally sets a PIN. Returns the new profile.</summary>
    public Profile Create(string name, string color, string initial, string? pin = null, string? avatar = null)
    {
        string? hash = null, salt = null;
        if (pin is not null) (hash, salt) = PinHasher.Hash(pin);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var id = conn.ExecuteScalar<int>(
            @"INSERT INTO profiles(name, color, avatar_initial, pin_hash, pin_salt, avatar, created_at)
              VALUES (@name, @color, @initial, @hash, @salt, @avatar, @now);
              SELECT last_insert_rowid();",
            new { name, color, initial, hash, salt, avatar, now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }, tx);
        CategoryRepo.SeedDefaults(conn, tx, id);
        tx.Commit();
        return Get(id)!;
    }

    /// <summary>
    /// First-run setup: in ONE transaction creates the profile (with its avatar and PIN hash), seeds the default categories under
    /// <paramref name="categoryNames"/> and writes the global settings (language, theme, last_profile_id...). Either all of it is saved or none.
    /// </summary>
    public Profile CreateFirst(string name, string color, string initial, string? pin, string? avatar,
        IReadOnlyList<string> categoryNames, IReadOnlyDictionary<string, string> globalSettings)
    {
        string? hash = null, salt = null;
        if (pin is not null) (hash, salt) = PinHasher.Hash(pin);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM profiles", transaction: tx) != 0)
        {
            tx.Rollback();
            throw new FirstRunAlreadyCompletedException();
        }
        var id = conn.ExecuteScalar<int>(
            @"INSERT INTO profiles(name, color, avatar_initial, pin_hash, pin_salt, avatar, created_at)
              VALUES (@name, @color, @initial, @hash, @salt, @avatar, @now);
              SELECT last_insert_rowid();",
            new { name, color, initial, hash, salt, avatar, now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }, tx);
        CategoryRepo.SeedDefaults(conn, tx, id, categoryNames);
        foreach (var (key, value) in globalSettings)
            conn.Execute(
                @"INSERT INTO app_settings(profile_id, key, value) VALUES (0, @key, @value)
                  ON CONFLICT(profile_id, key) DO UPDATE SET value=excluded.value",
                new { key, value }, tx);
        conn.Execute(
            @"INSERT INTO app_settings(profile_id, key, value) VALUES (0, 'last_profile_id', @value)
              ON CONFLICT(profile_id, key) DO UPDATE SET value=excluded.value",
            new { value = id.ToString(System.Globalization.CultureInfo.InvariantCulture) }, tx);
        tx.Commit();
        return Get(id)!;
    }

    /// <summary>Creates "Me" if there are no profiles at all. Returns true if it created one. (Tests and demo data only: the app shows first-run setup instead.)</summary>
    public bool EnsureDefaultProfile()
    {
        if (Count() > 0) return false;
        Create(DefaultProfileName, DefaultProfileColor, "M");
        return true;
    }

    public void Update(int id, string name, string color, string initial)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE profiles SET name=@name, color=@color, avatar_initial=@initial WHERE id=@id",
            new { id, name, color, initial });
    }

    /// <summary>Sets the picture ('builtin:name', 'file:avatars/id.png') or null for the coloured initial.</summary>
    public void SetAvatar(int id, string? avatar)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE profiles SET avatar=@avatar WHERE id=@id", new { id, avatar });
    }

    public void SetPin(int id, string pin)
    {
        var (hash, salt) = PinHasher.Hash(pin);
        using var conn = _db.Open();
        conn.Execute("UPDATE profiles SET pin_hash=@hash, pin_salt=@salt WHERE id=@id", new { id, hash, salt });
    }

    public void ClearPin(int id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE profiles SET pin_hash=NULL, pin_salt=NULL WHERE id=@id", new { id });
    }

    /// <summary>Raised after a profile was deleted, with its id (its toasts in Action Center are cleared).</summary>
    public event EventHandler<int>? Deleted;

    /// <summary>
    /// Deletes the profile; categories and reminders go with it via FK cascade. Also drops its per-profile settings.
    /// Returns false (and deletes nothing) if this is the only profile left.
    /// </summary>
    public bool Delete(int id)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM profiles", transaction: tx) <= 1) return false;
        conn.Execute("DELETE FROM app_settings WHERE profile_id=@id", new { id }, tx);
        var deleted = conn.Execute("DELETE FROM profiles WHERE id=@id", new { id }, tx);
        tx.Commit();
        if (deleted > 0)
        {
            AvatarService.DeleteFile(_avatarDir, id);
            Deleted?.Invoke(this, id);
        }
        return deleted > 0;
    }
}
