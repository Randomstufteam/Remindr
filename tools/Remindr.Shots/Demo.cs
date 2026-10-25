using System.Globalization;
using Microsoft.Data.Sqlite;
using Remindr.Data;
using Remindr.Services;

namespace Remindr.Shots;

/// <summary>
/// Fictional demo data only. Everything is written into a fresh folder under %TEMP%; the tool refuses any other location,
/// so it can never touch the real %AppData%\Remindr profile.
/// </summary>
internal static class Demo
{
    public static string NewTempDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "remindr-shots", tag + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Guard: only folders inside %TEMP% are ever used as a data folder.</summary>
    public static void EnsureSafe(string dir)
    {
        var full = Path.GetFullPath(dir);
        var temp = Path.GetFullPath(Path.GetTempPath());
        var appData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || full.StartsWith(appData, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to use a data folder outside %TEMP%: " + full);
    }

    private static SqliteConnection Create(string dir)
    {
        EnsureSafe(dir);
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "remindr.db"), Pooling = false, ForeignKeys = true }.ToString();
        var c = new SqliteConnection(cs);
        c.Open();
        foreach (var (v, sql) in Migrations.All.OrderBy(m => m.Version))
        {
            Exec(c, sql);
            Exec(c, "DELETE FROM schema_version");
            Exec(c, "INSERT INTO schema_version(version) VALUES (" + v + ")");
        }
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] p)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static void Setting(SqliteConnection c, string key, string value) =>
        Exec(c, "INSERT INTO app_settings(profile_id,key,value) VALUES (0,@k,@v) ON CONFLICT(profile_id,key) DO UPDATE SET value=excluded.value", ("@k", key), ("@v", value));

    /// <summary>An empty database (first run: greeting and setup) with only the language and theme preset.</summary>
    public static void SeedEmpty(string dir, string theme, string language)
    {
        using var c = Create(dir);
        Setting(c, "theme", theme);
        Setting(c, "language", language);
        Setting(c, "update_check_enabled", "0");
    }

    // Demo reminder text shown in the localisation screenshots (invented sample data, not app strings).
    private static readonly Dictionary<string, Dictionary<string, string>> Tx = new()
    {
        ["ja"] = new()
        {
            ["Pick up new glasses"] = "新しいメガネを受け取る", ["Soccer practice"] = "サッカーの練習", ["Study for the math test"] = "数学のテスト勉強",
            ["Dentist appointment"] = "歯医者の予約", ["Study group in the library"] = "図書館で勉強会", ["Math test: chapter 5"] = "数学のテスト（第5章）",
            ["Submit history essay"] = "歴史のレポートを提出", ["Chemistry lab report"] = "化学の実験レポート", ["Library books due"] = "図書館の本の返却日",
            ["Swim practice"] = "水泳の練習", ["Buy a gift for Mom"] = "お母さんへのプレゼントを買う", ["Mom's birthday"] = "お母さんの誕生日",
            ["Return the gym uniform"] = "体操服を返す", ["Bring shin guards and water"] = "すね当てと水を持っていく", ["Fractions and ratios. Calculator allowed."] = "分数と比。電卓は使えます。",
            ["Dr. Alvarez, second floor"] = "アルバレス先生、2階",
        },
        ["ar"] = new()
        {
            ["Pick up new glasses"] = "استلام النظارات الجديدة", ["Soccer practice"] = "تدريب كرة القدم", ["Study for the math test"] = "المذاكرة لاختبار الرياضيات",
            ["Dentist appointment"] = "موعد طبيب الأسنان", ["Study group in the library"] = "مجموعة دراسة في المكتبة", ["Math test: chapter 5"] = "اختبار الرياضيات: الفصل 5",
            ["Submit history essay"] = "تسليم مقال التاريخ", ["Chemistry lab report"] = "تقرير مختبر الكيمياء", ["Library books due"] = "موعد إرجاع كتب المكتبة",
            ["Swim practice"] = "تدريب السباحة", ["Buy a gift for Mom"] = "شراء هدية لأمي", ["Mom's birthday"] = "عيد ميلاد أمي",
            ["Return the gym uniform"] = "إرجاع زي الرياضة", ["Bring shin guards and water"] = "أحضر واقيات الساق والماء", ["Fractions and ratios. Calculator allowed."] = "الكسور والنسب. الآلة الحاسبة مسموحة.",
            ["Dr. Alvarez, second floor"] = "د. ألفاريز، الطابق الثاني",
        },
    };

    private static string Tr(string lang, string en) => Tx.TryGetValue(lang, out var m) && m.TryGetValue(en, out var t) ? t : en;

    /// <summary>Three invented profiles with reminders around today. Setup is skipped because profiles exist.</summary>
    public static void SeedProfiles(string dir, string theme, string language)
    {
        using var c = Create(dir);
        Setting(c, "theme", theme);
        Setting(c, "language", language);
        Setting(c, "update_check_enabled", "0");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string[] cats = { "School", "Work", "Personal" };
        string[] catColors = { "#3B82F6", "#F59E0B", "#10B981" };

        int AddProfile(string name, string color, string avatar, string? pin)
        {
            string? hash = null, salt = null;
            if (pin is not null) (hash, salt) = PinHasher.Hash(pin);
            Exec(c, "INSERT INTO profiles(name,color,avatar_initial,pin_hash,pin_salt,avatar,created_at) VALUES (@n,@c,@i,@h,@s,@a,@now)",
                ("@n", name), ("@c", color), ("@i", name[..1]), ("@h", hash), ("@s", salt), ("@a", avatar), ("@now", now));
            var id = (int)Scalar(c, "SELECT last_insert_rowid()");
            for (var i = 0; i < 3; i++)
                Exec(c, "INSERT INTO categories(profile_id,name,color,sort_order) VALUES (@p,@n,@c,@i)", ("@p", id), ("@n", cats[i]), ("@c", catColors[i]), ("@i", i));
            return id;
        }

        void R(int pid, string title, int cat, int dayOffset, string? time, string? icon, bool important = false, bool done = false, string? notes = null)
        {
            var catId = Scalar(c, $"SELECT id FROM categories WHERE profile_id={pid} AND sort_order={cat}");
            title = Tr(language, title); notes = notes is null ? null : Tr(language, notes);
            var date = today.AddDays(dayOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Exec(c, @"INSERT INTO reminders(profile_id,category_id,title,notes,due_date,due_time,is_important,is_completed,completed_at,icon,created_at,updated_at)
                      VALUES (@p,@cat,@t,@notes,@d,@tm,@imp,@done,@doneAt,@icon,@now,@now)",
                ("@p", pid), ("@cat", catId), ("@t", title), ("@notes", notes), ("@d", date), ("@tm", time),
                ("@imp", important ? 1 : 0), ("@done", done ? 1 : 0), ("@doneAt", done ? now : null), ("@icon", icon is null ? null : "builtin:" + icon), ("@now", now));
        }

        // Day of week helpers: next occurrence offsets of Tue/Thu around today so practice shows up in several weeks.
        int Offset(DayOfWeek dow, int weeksAhead)
        {
            var d = ((int)dow - (int)today.DayOfWeek + 7) % 7;
            return d + 7 * weeksAhead;
        }

        // ---- Maya, a student ----
        var maya = AddProfile("Maya", "#8B5CF6", "builtin:student", null);
        R(maya, "Soccer practice", 2, 0, "17:30", "soccer_ball", notes: "Bring shin guards and water");
        R(maya, "Pick up new glasses", 2, 0, "12:30", "glasses");
        R(maya, "Study for the math test", 0, 0, "20:00", "open_book", important: true);
        R(maya, "Dentist appointment", 2, 1, "16:30", "tooth", important: true, notes: "Dr. Alvarez, second floor");
        R(maya, "Study group in the library", 0, 1, "18:00", "books");
        R(maya, "Math test: chapter 5", 0, 2, "09:00", "abacus", important: true, notes: "Fractions and ratios. Calculator allowed.");
        R(maya, "Submit history essay", 0, 3, "23:59", "memo", important: true);
        R(maya, "Chemistry lab report", 0, 4, "10:15", "microscope");
        R(maya, "Library books due", 0, 5, null, "blue_book");
        R(maya, "Swim practice", 2, 6, "07:00", "person_swimming");
        R(maya, "Buy a gift for Mom", 2, 7, null, "wrapped_gift");
        R(maya, "Mom's birthday", 2, 9, null, "birthday_cake", important: true, notes: "Dinner at 7, bring the cake");
        R(maya, "Bake cookies for the bake sale", 2, 10, "15:00", "cooking");
        R(maya, "Science fair project due", 0, 12, null, "microscope", important: true);
        R(maya, "Field trip permission slip", 0, 14, null, "clipboard");
        R(maya, "Concert tickets go on sale", 2, 17, "10:00", "ticket");
        R(maya, "Winter break starts", 2, 24, null, "beach_with_umbrella");
        R(maya, "Return library book", 0, -2, "15:30", "books", done: true);
        R(maya, "Physics quiz", 0, -4, "11:00", "pencil", done: true);
        R(maya, "Pack the gym bag", 2, -1, "07:30", "backpack", done: true);
        R(maya, "Order class photos", 0, -1, null, "envelope", done: true);
        R(maya, "Soccer practice", 2, -3, "17:30", "soccer_ball", done: true);
        foreach (var wk in new[] { -3, -2, -1, 0, 1, 2 })
            foreach (var dow in new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday })
            {
                var off = Offset(dow, wk);
                if (off == 0 || off == -3) continue; // today and the one already added above
                R(maya, "Soccer practice", 2, off, "17:30", "soccer_ball", done: off < 0);
            }
        // Past school days (done) so earlier weeks of the month are not empty.
        R(maya, "Biology homework", 0, -5, "19:00", "microscope", done: true);
        R(maya, "Orchestra rehearsal", 2, -6, "16:00", "ticket", done: true);
        R(maya, "Return the gym uniform", 0, 2, "08:15", "backpack");
        R(maya, "Clean my room", 2, -8, null, "broom", done: true);
        R(maya, "Spanish vocabulary quiz", 0, -9, "10:30", "pencil", done: true);
        R(maya, "Grandma's video call", 2, -11, "18:00", "mobile_phone", done: true);
        R(maya, "Submit the art project", 0, -13, null, "memo", done: true);
        R(maya, "Eye exam", 2, -15, "14:00", "glasses", done: true);
        R(maya, "Book fair", 0, -17, null, "books", done: true);
        R(maya, "Buy a new notebook", 0, -19, null, "notebook", done: true);
        R(maya, "Start of the new term", 0, -22, null, "school", done: true);
        R(maya, "Visit the new dentist", 2, 19, "16:00", "tooth");
        R(maya, "Winter concert", 2, 30, "19:00", "ticket", important: true);

        // ---- Jordan, a parent ----
        var jordan = AddProfile("Jordan", "#10B981", "builtin:cook", null);
        R(jordan, "Grocery run", 2, 0, "18:00", "shopping_cart", notes: "Milk, eggs, bread, apples");
        R(jordan, "Team standup", 1, 1, "09:30", "laptop");
        R(jordan, "Team standup", 1, 3, "09:30", "laptop");
        R(jordan, "Team standup", 1, 5, "09:30", "laptop");
        R(jordan, "Parent-teacher meeting", 0, 3, "14:30", "school", important: true);
        R(jordan, "Soccer carpool", 2, 2, "17:00", "soccer_ball");
        R(jordan, "Quarterly report due", 1, 4, "17:00", "chart_increasing", important: true);
        R(jordan, "Water the plants", 2, 1, "08:00", "potted_plant");
        R(jordan, "Renew car insurance", 2, 8, null, "credit_card");
        R(jordan, "Pay rent", 2, Math.Max(1, (new DateOnly(today.AddMonths(1).Year, today.AddMonths(1).Month, 1).DayNumber - today.DayNumber)), null, "money_bag", important: true);
        R(jordan, "Dentist checkup for the kids", 2, 11, "15:45", "tooth");
        R(jordan, "Flight to Lisbon", 2, 20, "06:55", "airplane", important: true, notes: "Gate opens 06:05, passports in the blue folder");
        R(jordan, "Book the hotel", 2, -5, null, "hotel", done: true);
        R(jordan, "Call the plumber", 2, -2, "09:00", "toolbox", done: true);

        // ---- Sam ----
        var sam = AddProfile("Sam", "#F59E0B", "builtin:panda", "2468");
        R(sam, "Basketball game", 2, 2, "16:00", "basketball");
        R(sam, "Finish the book report", 0, 4, null, "memo", important: true);
        R(sam, "Dog vet checkup", 2, 6, "11:30", "dog_face");
        R(sam, "Piano recital", 2, 13, "18:30", "ticket");
        R(sam, "Return the school laptop", 0, 5, "08:30", "laptop", done: true);

        Setting(c, "last_profile_id", maya.ToString(CultureInfo.InvariantCulture));
    }
}


