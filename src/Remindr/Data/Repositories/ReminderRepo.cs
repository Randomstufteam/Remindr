using System.Globalization;
using Dapper;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.Data.Repositories;

/// <summary>
/// Reminder persistence. Every query is scoped to <see cref="ProfileSession.Current"/>, so one profile can never see or
/// change another's reminders (writes by id also filter on profile_id).
/// </summary>
public sealed class ReminderRepo
{
    private const string Select =
        @"SELECT id AS Id, profile_id AS ProfileId, category_id AS CategoryId, title AS Title, notes AS Notes,
                 due_date AS DueDate, due_time AS DueTime, is_important AS IsImportant, is_completed AS IsCompleted,
                 completed_at AS CompletedAt, snoozed_until AS SnoozedUntil, icon AS Icon, created_at AS CreatedAt, updated_at AS UpdatedAt
          FROM reminders";

    // all-day (NULL time) first, then by time, then insertion order
    private const string Order = " ORDER BY due_date, due_time IS NOT NULL, due_time, id";

    private readonly Db _db;
    private readonly ProfileSession _session;
    private readonly IClock _clock;

    public ReminderRepo(Db db, ProfileSession session, IClock clock)
    {
        _db = db;
        _session = session;
        _clock = clock;
    }

    /// <summary>Raised after any write (create/update/delete/complete/important) so open lists can refresh.</summary>
    public event EventHandler? Changed;

    private int ProfileId =>
        _session.Current?.Id ?? throw new InvalidOperationException("No profile is selected.");

    private static string D(DateOnly d) => d.ToString(TypeHandlers.DateFormat, CultureInfo.InvariantCulture);
    private static string T(TimeOnly t) => t.ToString(TypeHandlers.TimeFormat, CultureInfo.InvariantCulture);
    private string Stamp() => _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Inserts <paramref name="r"/> into the current profile and returns the stored row.</summary>
    public Reminder Create(Reminder r)
    {
        var title = (r.Title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("A reminder needs a title.", nameof(r));

        var pid = ProfileId;
        var now = Stamp();
        using var conn = _db.Open();
        var id = conn.ExecuteScalar<int>(
            @"INSERT INTO reminders(profile_id, category_id, title, notes, due_date, due_time, is_important,
                                    is_completed, completed_at, icon, created_at, updated_at)
              VALUES (@pid, @cat, @title, @notes, @date, @time, @imp, @done, @doneAt, @icon, @now, @now);
              SELECT last_insert_rowid();",
            new
            {
                pid, cat = r.CategoryId, title, notes = NormalizeNotes(r.Notes),
                date = D(r.DueDate), time = r.DueTime is { } t ? T(t) : null,
                imp = r.IsImportant ? 1 : 0, done = r.IsCompleted ? 1 : 0,
                doneAt = r.IsCompleted ? now : null, icon = NormalizeIcon(r.Icon), now
            });
        RaiseChanged();
        return Get(id)!;
    }

    /// <summary>Updates the editable fields (title, notes, date, time, category, important, icon). Completion is separate. False if not found.</summary>
    public bool Update(Reminder r)
    {
        var title = (r.Title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("A reminder needs a title.", nameof(r));

        int n;
        using (var conn = _db.Open())
        {
            n = conn.Execute(
                @"UPDATE reminders SET category_id=@cat, title=@title, notes=@notes, due_date=@date, due_time=@time,
                                       is_important=@imp, icon=@icon, updated_at=@now,
                                       snoozed_until = CASE WHEN due_date <> @date OR IFNULL(due_time, '') <> IFNULL(@time, '')
                                                            THEN NULL ELSE snoozed_until END
                  WHERE id=@id AND profile_id=@pid",
                new
                {
                    icon = NormalizeIcon(r.Icon), id = r.Id, pid = ProfileId, cat = r.CategoryId, title, notes = NormalizeNotes(r.Notes),
                    date = D(r.DueDate), time = r.DueTime is { } t ? T(t) : null,
                    imp = r.IsImportant ? 1 : 0, now = Stamp()
                });
        }
        if (n > 0) RaiseChanged();
        return n > 0;
    }

    public bool Delete(int id)
    {
        var before = Get(id);
        int n;
        using (var conn = _db.Open())
            n = conn.Execute("DELETE FROM reminders WHERE id=@id AND profile_id=@pid", new { id, pid = ProfileId });
        if (n > 0)
        {
            RaiseChanged();
            if (before is not null) Closed?.Invoke(this, before);
        }
        return n > 0;
    }

    /// <summary>Raised (after <see cref="Changed"/>) when a reminder is completed or deleted, with its last state, so its toasts can be cleared.</summary>
    public event EventHandler<Reminder>? Closed;

    /// <param name="profileId">Profile to look in; null = the current profile. Toasts name their own profile.</param>
    public Reminder? Get(int id, int? profileId = null)
    {
        using var conn = _db.Open();
        return conn.QuerySingleOrDefault<Reminder>(Select + " WHERE id=@id AND profile_id=@pid",
            new { id, pid = profileId ?? ProfileId });
    }

    public IReadOnlyList<Reminder> ListAll()
    {
        using var conn = _db.Open();
        return conn.Query<Reminder>(Select + " WHERE profile_id=@pid" + Order, new { pid = ProfileId }).ToList();
    }

    /// <summary>Reminders due from <paramref name="from"/> to <paramref name="to"/>, both inclusive (completed ones included).</summary>
    public IReadOnlyList<Reminder> ListByRange(DateOnly from, DateOnly to)
    {
        using var conn = _db.Open();
        return conn.Query<Reminder>(
            Select + " WHERE profile_id=@pid AND due_date BETWEEN @from AND @to" + Order,
            new { pid = ProfileId, from = D(from), to = D(to) }).ToList();
    }

    /// <summary>Incomplete reminders due before <paramref name="now"/> (an all-day reminder is overdue from the next day).</summary>
    public IReadOnlyList<Reminder> ListOverdue(DateTime now)
    {
        var (today, time) = ReminderQueries.OverdueCutoff(now);
        using var conn = _db.Open();
        return conn.Query<Reminder>(
            Select + @" WHERE profile_id=@pid AND is_completed=0
                        AND (due_date < @today OR (due_date = @today AND due_time IS NOT NULL AND due_time < @time))" + Order,
            new { pid = ProfileId, today = D(today), time = T(time) }).ToList();
    }

    /// <summary>Marks complete (completed_at = now, UTC) or incomplete (completed_at cleared).</summary>
    public bool SetCompleted(int id, bool completed, int? profileId = null)
    {
        int n;
        using (var conn = _db.Open())
            n = conn.Execute(
                "UPDATE reminders SET is_completed=@done, completed_at=@at, updated_at=@now WHERE id=@id AND profile_id=@pid",
                new { id, pid = profileId ?? ProfileId, done = completed ? 1 : 0, at = completed ? Stamp() : null, now = Stamp() });
        if (n > 0)
        {
            RaiseChanged();
            if (completed && Get(id, profileId) is { } r) Closed?.Invoke(this, r);
        }
        return n > 0;
    }

    /// <summary>Silences alerts until <paramref name="until"/> (local time, minute precision); null clears the snooze.</summary>
    public bool SetSnoozedUntil(int id, DateTime? until, int? profileId = null)
    {
        int n;
        using (var conn = _db.Open())
            n = conn.Execute(
                "UPDATE reminders SET snoozed_until=@until, updated_at=@now WHERE id=@id AND profile_id=@pid",
                new
                {
                    id, pid = profileId ?? ProfileId, now = Stamp(),
                    until = until?.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
                });
        if (n > 0) RaiseChanged();
        return n > 0;
    }

    /// <summary>Moves the due date (time kept) and clears any snooze, since the occurrence changed.</summary>
    public bool MoveToDate(int id, DateOnly date, int? profileId = null)
    {
        int n;
        using (var conn = _db.Open())
            n = conn.Execute(
                "UPDATE reminders SET due_date=@date, snoozed_until=NULL, updated_at=@now WHERE id=@id AND profile_id=@pid",
                new { id, pid = profileId ?? ProfileId, date = D(date), now = Stamp() });
        if (n > 0) RaiseChanged();
        return n > 0;
    }

    public bool SetImportant(int id, bool important)
    {
        int n;
        using (var conn = _db.Open())
            n = conn.Execute(
                "UPDATE reminders SET is_important=@imp, updated_at=@now WHERE id=@id AND profile_id=@pid",
                new { id, pid = ProfileId, imp = important ? 1 : 0, now = Stamp() });
        if (n > 0) RaiseChanged();
        return n > 0;
    }

    private static string? NormalizeIcon(string? icon) => string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();

    private static string? NormalizeNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

