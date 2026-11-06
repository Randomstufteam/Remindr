using Remindr.Data.Repositories;

namespace Remindr.Services.Assistant;

/// <summary>The voice settings (global): Voice input is off by default; Speak replies and the Windows voice only matter once it is on.</summary>
public sealed class VoiceSettings
{
    public const string EnabledKey = "assistant_voice_enabled";
    public const string SpeakKey = "assistant_speak_replies";
    public const string VoiceKey = "assistant_voice_name";

    private readonly SettingsRepo _settings;

    public VoiceSettings(SettingsRepo settings) => _settings = settings;

    public event EventHandler? Changed;

    public bool Enabled
    {
        get => _settings.Get(EnabledKey) == "1";
        set { _settings.Set(EnabledKey, value ? "1" : "0"); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>On unless switched off.</summary>
    public bool SpeakReplies
    {
        get => _settings.Get(SpeakKey) != "0";
        set { _settings.Set(SpeakKey, value ? "1" : "0"); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>The chosen Windows voice's display name, or null for the Windows default.</summary>
    public string? VoiceName
    {
        get => _settings.Get(VoiceKey) is { Length: > 0 } v ? v : null;
        set { _settings.Set(VoiceKey, value ?? ""); Changed?.Invoke(this, EventArgs.Empty); }
    }
}
