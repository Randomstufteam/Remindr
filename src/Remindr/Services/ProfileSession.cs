using Remindr.Data.Repositories;
using Remindr.Models;

namespace Remindr.Services;

/// <summary>Holds the current profile. Anything that needs profile data reads it from here.</summary>
public sealed class ProfileSession
{
    public const string LastProfileKey = "last_profile_id";

    private readonly ProfileRepo _profiles;
    private readonly SettingsRepo _settings;

    public ProfileSession(ProfileRepo profiles, SettingsRepo settings)
    {
        _profiles = profiles;
        _settings = settings;
    }

    public Profile? Current { get; private set; }

    public event EventHandler? CurrentProfileChanged;

    public int? LastProfileId =>
        int.TryParse(_settings.Get(LastProfileKey), out var id) ? id : null;

    public void SwitchTo(Profile profile)
    {
        Current = profile;
        _settings.Set(LastProfileKey, profile.Id.ToString());
        CurrentProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads the current profile after an edit (name/color/PIN) and raises the change event.</summary>
    public void RefreshCurrent()
    {
        if (Current is null) return;
        var fresh = _profiles.Get(Current.Id);
        if (fresh is null) { Clear(); return; }
        Current = fresh;
        CurrentProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>No current profile (e.g. it was deleted). The shell shows the picker.</summary>
    public void Clear()
    {
        Current = null;
        CurrentProfileChanged?.Invoke(this, EventArgs.Empty);
    }
}
