using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Remindr.Services;

/// <summary>Imports a user's own picture: PNG/JPG, centre-cropped to a square and scaled to 256x256.</summary>
public sealed class AvatarService
{
    public const int Size = 256;

    /// <summary>The avatars folder under the data folder (REMINDR_DATA_DIR aware, see <see cref="AppPaths"/>).</summary>
    public static string DefaultDirectory { get; } = AppPaths.Current.AvatarsDir;

    private readonly string _dir;

    public AvatarService(string? directory = null) => _dir = directory ?? DefaultDirectory;

    public string DirectoryPath => _dir;

    /// <summary>The value stored in profiles.avatar for an imported picture.</summary>
    public static string StoredValue(int profileId) =>
        string.Create(CultureInfo.InvariantCulture, $"file:avatars/{profileId}.png");

    public string PathFor(int profileId) => Path.Combine(_dir, profileId.ToString(CultureInfo.InvariantCulture) + ".png");

    /// <summary>
    /// Decodes <paramref name="sourcePath"/>, centre-crops to a square, scales to 256x256 (transparency kept)
    /// and saves it as {dir}\{id}.png. Returns the value to store in profiles.avatar. Throws if the file is not a readable image.
    /// </summary>
    public string ImportFile(int profileId, string sourcePath)
    {
        var png = ToAvatarPng(sourcePath);
        System.IO.Directory.CreateDirectory(_dir);
        var target = PathFor(profileId);
        var tmp = target + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Move(tmp, target, overwrite: true);
        return StoredValue(profileId);
    }

    /// <summary>Crop/scale to 256x256 and return PNG bytes. Also used for previews before the profile exists.</summary>
    public static byte[] ToAvatarPng(string sourcePath)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.UriSource = new Uri(Path.GetFullPath(sourcePath));
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        src.EndInit();
        src.Freeze();

        int w = src.PixelWidth, h = src.PixelHeight;
        if (w <= 0 || h <= 0) throw new InvalidDataException("The image is empty.");
        var side = Math.Min(w, h);
        var square = new CroppedBitmap(src, new Int32Rect((w - side) / 2, (h - side) / 2, side, side));

        // Render into a fixed 256x256 surface so the result is exactly that size, with alpha.
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(square, new Rect(0, 0, Size, Size));
        var rtb = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Removes an imported picture. Never throws.</summary>
    public static void DeleteFile(string directory, int profileId)
    {
        try
        {
            var p = Path.Combine(directory, profileId.ToString(CultureInfo.InvariantCulture) + ".png");
            if (File.Exists(p)) File.Delete(p);
        }
        catch (Exception) { /* a leftover picture is harmless */ }
    }
}
