using System.Globalization;
using Dapper;
using Remindr.Services;

namespace Remindr.Data.Repositories;

/// <summary>
/// notification_log persistence: what fired or was acknowledged per reminder occurrence. Not profile-scoped
/// (reminder ids are unique across profiles); the scheduler works for every profile.
/// </summary>
public sealed class NotificationLogRepo
{
    private const string StampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private readonly Db _db;

    public NotificationLogRepo(Db db) => _db = db;

    /// <summary>Writes an entry. A duplicate (same reminder, occurrence, kind and second) is ignored; returns false then.</summary>
    public bool Insert(NotificationLogEntry e)
    {
        using var conn = _db.Open();
        return conn.Execute(
            @"INSERT OR IGNORE INTO notification_log(reminder_id, occurrence, kind, fired_at)
              VALUES (@id, @occ, @kind, @at)",
            new
            {
                id = e.ReminderId, occ = e.Occurrence, kind = (int)e.Kind,
                at = e.FiredAtUtc.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture)
            }) > 0;
    }

    /// <summary>Entries for one reminder occurrence, oldest first. This is the list <see cref="NotificationPlanner.GetDue"/> takes.</summary>
    public IReadOnlyList<NotificationLogEntry> Get(long reminderId, string occurrence)
    {
        using var conn = _db.Open();
        return conn.Query<(long Id, string Occ, int Kind, string At)>(
                @"SELECT reminder_id AS Id, occurrence AS Occ, kind AS Kind, fired_at AS At
                  FROM notification_log WHERE reminder_id=@reminderId AND occurrence=@occurrence
                  ORDER BY fired_at, id",
                new { reminderId, occurrence })
            .Select(r => new NotificationLogEntry(r.Id, r.Occ, (NotificationKind)r.Kind,
                DateTime.SpecifyKind(DateTime.ParseExact(r.At, StampFormat, CultureInfo.InvariantCulture), DateTimeKind.Utc)))
            .ToList();
    }
}
