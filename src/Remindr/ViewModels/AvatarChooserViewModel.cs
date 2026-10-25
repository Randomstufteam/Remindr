using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>
/// State of the avatar chooser: the built-in pictures (category chips + search, see <see cref="Picker"/>),
/// "Your own picture..." and "Initial only".
/// Nothing is written until the owning dialog is saved: the choice is either a stored value (<see cref="Avatar"/>)
/// or a picked file (<see cref="UploadPath"/>, previewed through <see cref="PreviewImage"/>).
/// </summary>
public sealed partial class AvatarChooserViewModel : ObservableObject
{
    public AvatarChooserViewModel(string? currentAvatar, string color = "#3B82F6", string initial = "?")
    {
        _avatar = currentAvatar;
        Color = color;
        Initial = initial;
        Picker = new PicturePickerViewModel(
            AvatarCatalog.Available,
            Enum.GetValues<AvatarCategory>().Select(c => (PictureCatalog.CategoryLabel(c), (Func<PictureEntry, bool>)(e => e.AvatarGroup == c))),
            currentAvatar);
        Picker.Chosen += Choose;
        SyncSelection();
    }

    public PicturePickerViewModel Picker { get; }

    /// <summary>Colour/letter for the standalone preview (the Change picture dialog); the edit dialog has its own preview.</summary>
    public string Color { get; }
    public string Initial { get; }

    /// <summary>The stored value to use ('builtin:x', 'file:avatars/id.png' or null). Ignored while <see cref="UploadPath"/> is set.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPicture))]
    private string? _avatar;

    /// <summary>A picked image file waiting to be imported on Save.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUpload))]
    private string? _uploadPath;

    /// <summary>Cropped 256x256 preview of <see cref="UploadPath"/>.</summary>
    [ObservableProperty] private ImageSource? _previewImage;

    [ObservableProperty] private string? _error;

    public bool HasUpload => UploadPath is not null;
    public bool HasPicture => HasUpload || !string.IsNullOrEmpty(Avatar);

    public void Choose(PictureOption option)
    {
        Error = null;
        UploadPath = null;
        PreviewImage = null;
        Avatar = option.Entry.Value;
        SyncSelection();
    }

    public void UseInitialOnly()
    {
        Error = null;
        UploadPath = null;
        PreviewImage = null;
        Avatar = null;
        SyncSelection();
    }

    /// <summary>Loads and crops <paramref name="path"/> for the preview. Returns false (with <see cref="Error"/> set) if it isn't a readable image.</summary>
    public bool TryUpload(string path)
    {
        try
        {
            var bytes = AvatarService.ToAvatarPng(path);
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            PreviewImage = bmp;
            UploadPath = path;
            Error = null;
            SyncSelection();
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Avatar upload preview");
            Error = Loc.Get("Avatar_Error_Read");
            return false;
        }
    }

    private void SyncSelection() => Picker.SelectValue(HasUpload ? null : Avatar);
}

