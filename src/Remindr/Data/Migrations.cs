namespace Remindr.Data;

public static class Migrations
{
    public static readonly (int Version, string Sql)[] All =
    {
        (1, V1),
        (2, V2),
        (3, V3)
    };

    public static int Latest => All[^1].Version;

    /// <summary>Phase 2.5: profile pictures. 'builtin:&lt;name&gt;' | 'file:avatars/&lt;id&gt;.png' | NULL = coloured initial.</summary>
    private const string V2 = @"
ALTER TABLE profiles ADD COLUMN avatar TEXT NULL;
";

    /// <summary>Phase 3.5: optional reminder picture. 'builtin:&lt;key&gt;' | NULL = no picture.</summary>
    private const string V3 = @"
ALTER TABLE reminders ADD COLUMN icon TEXT NULL;
";

    private const string V1 = @"
CREATE TABLE schema_version (version INTEGER NOT NULL);

CREATE TABLE profiles (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  color TEXT NOT NULL,
  avatar_initial TEXT NOT NULL,
  pin_hash TEXT NULL,
  pin_salt TEXT NULL,
  created_at TEXT NOT NULL);

CREATE TABLE categories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  profile_id INTEGER NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
  name TEXT NOT NULL, color TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0,
  UNIQUE(profile_id, name));

CREATE TABLE reminders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  profile_id INTEGER NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
  category_id INTEGER NULL REFERENCES categories(id) ON DELETE SET NULL,
  title TEXT NOT NULL,
  notes TEXT NULL,
  due_date TEXT NOT NULL,
  due_time TEXT NULL,
  is_important INTEGER NOT NULL DEFAULT 0,
  is_completed INTEGER NOT NULL DEFAULT 0,
  completed_at TEXT NULL,
  snoozed_until TEXT NULL,
  recurrence_type INTEGER NOT NULL DEFAULT 0,
  recurrence_interval INTEGER NOT NULL DEFAULT 1,
  recurrence_days INTEGER NOT NULL DEFAULT 0,
  recurrence_end_date TEXT NULL,
  created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE INDEX ix_rem_profile_date ON reminders(profile_id, due_date);

CREATE TABLE notification_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  reminder_id INTEGER NOT NULL REFERENCES reminders(id) ON DELETE CASCADE,
  occurrence TEXT NOT NULL,
  kind INTEGER NOT NULL,
  fired_at TEXT NOT NULL,
  UNIQUE(reminder_id, occurrence, kind, fired_at));
CREATE INDEX ix_nlog ON notification_log(reminder_id, occurrence);

CREATE TABLE app_settings (
  profile_id INTEGER NOT NULL DEFAULT 0,
  key TEXT NOT NULL, value TEXT NOT NULL,
  PRIMARY KEY(profile_id, key));
";
}

