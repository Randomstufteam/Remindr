using System.Globalization;
using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>
/// The once-a-day summary toast. Settings are global app_settings (like close_to_tray): <c>daily_summary_enabled</c> (default off),
/// <c>daily_summary_time</c> ("HH:mm", default 08:00) and <c>last_daily_summary_date</c>. The scheduler calls <see cref="TryShowAsync"/>
/// every tick; the date is written BEFORE the toast, so a relaunch (or a crash) never repeats it. The text is the current profile's.
/// </summary>
public sealed class DailySummaryService
{
    public const string EnabledKey = "daily_summary_enabled";
    public const string TimeKey = "daily_summary_time";
    public const string LastDateKey = "last_daily_summary_date";
    public static readonly TimeOnly DefaultTime = new(8, 0);

    private readonly SettingsRepo _settings;
    private readonly ReminderRepo _reminders;
    private readonly ProfileSession _session;

    public DailySummaryService(SettingsRepo settings, ReminderRepo reminders, ProfileSession session)
    {
        _settings = settings;
        _reminders = reminders;
        _session = session;
    }

    public bool Enabled
    {
        get => _settings.Get(EnabledKey) == "1";
        set => _settings.Set(EnabledKey, value ? "1" : "0");
    }

    public TimeOnly Time
    {
        get => TimeOnly.TryParseExact(_settings.Get(TimeKey), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DefaultTime;
        set => _settings.Set(TimeKey, value.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>Shows the toast when it is on, the time has passed today, and today's has not been shown. Returns true when it showed.</summary>
    public async Task<bool> TryShowAsync(DateTime now, IToastSink sink, CancellationToken ct)
    {
        if (!Enabled || _session.Current is null) return false;
        var today = DateOnly.FromDateTime(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_settings.Get(LastDateKey) == today || TimeOnly.FromDateTime(now) < Time) return false;

        var text = SummaryService.Sentence(SummaryService.Buckets(_reminders.ListAll(), now));
        _settings.Set(LastDateKey, today);
        await sink.ShowSummaryAsync(Loc.Get("Settings_DailySummary_Title"), text, ct);
        return true;
    }
}
