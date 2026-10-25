using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Models;

namespace Remindr.ViewModels;

public sealed partial class ProfileTileViewModel : ObservableObject
{
    public ProfileTileViewModel(Profile profile, bool isLast, bool canDelete)
    {
        Profile = profile;
        IsLast = isLast;
        CanDelete = canDelete;
    }

    public Profile Profile { get; }
    public string Name => Profile.Name;
    public string Color => Profile.Color;
    public string Initial => Profile.AvatarInitial;
    public string? Avatar => Profile.Avatar;
    public bool HasPin => Profile.HasPin;
    public bool IsLast { get; }
    public bool CanDelete { get; }
}
