using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>
/// Decides when the bundled What's New page shows by itself: once per version, after an update. The last version it was shown for is
/// the global setting <c>whatsnew_last_seen</c>. A brand-new user (no profile yet) never sees it, and a downgrade never shows it.
/// </summary>
public sealed class WhatsNewService
{
    public const string LastSeenKey = "whatsnew_last_seen";

    private readonly SettingsRepo _settings;
    private readonly ProfileRepo _profiles;

    public WhatsNewService(SettingsRepo settings, ProfileRepo profiles)
    {
        _settings = settings;
        _profiles = profiles;
    }

    /// <summary>Raised when something (Settings) asks for the bundled page to be shown now.</summary>
    public event EventHandler? ShowRequested;

    public void RequestShow() => ShowRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>"0.4.0" as "0.4" (the design-language style heading), "0.4.1" stays "0.4.1".</summary>
    public static string DisplayVersion(string? version)
    {
        var v = WhatsNewCatalog.ParseVersion(version);
        return v is null ? (version ?? "") : v.Build == 0 ? $"{v.Major}.{v.Minor}" : v.ToString(3);
    }

    /// <summary>Debug-only <c>--whatsnew</c>: show the page after the profile opens even if it was seen.</summary>
    public static bool ForceShow { get; set; }

    /// <summary>
    /// The version the app counts as running. The real version, unless a Debug build was started with <c>--pretend-version</c> (so the
    /// 0.4.0 page can be tried before the version number is raised).
    /// </summary>
    public static string CurrentVersion { get; set; } = AppInfo.Version;

    /// <summary>
    /// The pure rule. Shows when the page has entries for <paramref name="current"/> and current is newer than <paramref name="lastSeen"/>.
    /// No stored version while profiles exist means an upgrade from before this setting existed (0.3.x), so it shows; no stored version
    /// and no profile is a first run (setup writes the version when it finishes), so it does not.
    /// </summary>
    public static bool ShouldShow(string? current, string? lastSeen, bool profilesExist, bool hasEntries)
    {
        if (!hasEntries) return false;
        var now = WhatsNewCatalog.ParseVersion(current);
        if (now is null) return false;
        if (string.IsNullOrWhiteSpace(lastSeen)) return profilesExist;
        // An unreadable stored value counts as not seen.
        return WhatsNewCatalog.ParseVersion(lastSeen) is not { } seen || now > seen;
    }

    /// <summary>The rule applied to the database and the catalog. Never throws: a failure means "do not show".</summary>
    public bool ShouldShowNow()
    {
        try
        {
            return ShouldShow(CurrentVersion, _settings.Get(LastSeenKey), _profiles.Count() > 0, WhatsNewCatalog.HasEntries(CurrentVersion));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "What's New check");
            return false;
        }
    }

    /// <summary>"Got it" or Esc: remembers the current version, never lowering a newer stored one (a downgrade stays quiet).</summary>
    public void MarkSeen()
    {
        try
        {
            var stored = WhatsNewCatalog.ParseVersion(_settings.Get(LastSeenKey));
            var now = WhatsNewCatalog.ParseVersion(CurrentVersion);
            if (stored is not null && now is not null && stored > now) return;
            _settings.Set(LastSeenKey, CurrentVersion);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Saving the What's New version"); }
    }
}
