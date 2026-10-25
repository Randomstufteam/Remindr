using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.ViewModels;

public enum PinAction { Keep, Set, Remove }

/// <summary>State and validation for the create/edit profile dialog.</summary>
public partial class ProfileEditViewModel : ObservableObject
{
    public const int MaxNameLength = 30;

    public static readonly IReadOnlyList<string> Palette = new[]
    {
        "#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#F97316", "#F59E0B", "#10B981", "#14B8A6"
    };

    private bool _touched;
    private bool _settingInitial;
    private bool _initialEditedByUser;

    public ProfileEditViewModel(Profile? existing, string defaultColor)
    {
        Existing = existing;
        HadPin = existing?.HasPin ?? false;
        _name = existing?.Name ?? "";
        _color = existing?.Color ?? defaultColor;
        _initial = existing?.AvatarInitial ?? "?";
        _initialEditedByUser = existing is not null && existing.AvatarInitial != DeriveInitial(existing.Name);
        if (existing is null) _initial = "?";
        Chooser = new AvatarChooserViewModel(existing?.Avatar);
    }

    public Profile? Existing { get; }
    public bool IsNew => Existing is null;
    /// <summary>The picture chooser; its choice drives the live preview and is saved with the profile.</summary>
    public AvatarChooserViewModel Chooser { get; }
    public bool HadPin { get; }
    public IReadOnlyList<string> Colors => Palette;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error), nameof(ShownError))]
    private string _name;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error))]
    private string _initial;

    [ObservableProperty] private string _color;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error), nameof(IsSettingPin))]
    private PinAction _pinAction = PinAction.Keep;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error))]
    private string _newPin = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(Error))]
    private string _confirmPin = "";

    public bool IsSettingPin => PinAction == PinAction.Set;

    partial void OnNameChanged(string value)
    {
        _touched = true;
        if (_initialEditedByUser) return;
        _settingInitial = true;
        Initial = DeriveInitial(value);
        _settingInitial = false;
    }

    partial void OnInitialChanged(string value)
    {
        if (_settingInitial) return;
        _initialEditedByUser = value.Length > 0;
    }

    public static string DeriveInitial(string? name)
    {
        var t = (name ?? "").Trim();
        return t.Length == 0 ? "?" : t.Substring(0, char.IsSurrogate(t[0]) && t.Length > 1 ? 2 : 1).ToUpperInvariant();
    }

    public string? Error
    {
        get
        {
            var n = Name.Trim();
            if (n.Length == 0) return Loc.Get("Profile_Error_Name");
            if (n.Length > MaxNameLength) return Loc.Format("Profile_Error_NameLength", MaxNameLength);
            if (string.IsNullOrWhiteSpace(Initial)) return Loc.Get("Profile_Error_Initial");
            if (PinAction == PinAction.Set)
            {
                if (!PinHasher.IsValidPin(NewPin)) return Loc.Get("Profile_Error_PinDigits");
                if (NewPin != ConfirmPin) return Loc.Get("Pin_Error_Mismatch");
            }
            return null;
        }
    }

    public bool IsValid => Error is null;

    /// <summary>Error text for display; hides the empty-name error until the name has been edited.</summary>
    public string? ShownError => (!_touched && Name.Trim().Length == 0) ? null : Error;

    public string FinalName => Name.Trim();
    public string FinalInitial => Initial.Trim().ToUpperInvariant();
}


