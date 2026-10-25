using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Remindr.Services;

/// <summary>Avatar view of the shared <see cref="PictureCatalog"/>, plus image loading for profile avatars.</summary>
public static class AvatarCatalog
{
    public const string BuiltinPrefix = PictureCatalog.BuiltinPrefix;
    public const string FilePrefix = "file:";

    static AvatarCatalog()
    {
        // Registers the pack:// scheme even when no Application exists yet (unit tests, early startup).
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        // pack://application:,,, resolves only after Application's static constructor has registered the app's resource
        // package. Application.GetResourceStream triggers it, BitmapImage does not: without this, a picture decoded before
        // anything touched Application failed (and the null was cached). Unit tests hit this depending on test order.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
    }

    /// <summary>All avatar pictures in display order, whether or not their image is present.</summary>
    public static IReadOnlyList<PictureEntry> All { get; } = PictureCatalog.ForAvatars().OrderBy(e => e.AvatarGroup).ToList(); // category order (People first), manifest order within

    private static IReadOnlyList<PictureEntry>? _available;

    /// <summary>Entries whose image resource exists. Probed once; a missing file just hides that entry.</summary>
    public static IReadOnlyList<PictureEntry> Available
    {
        get
        {
            lock (AvailableLock) // pack:// resource lookups raced when two threads probed at once
                return _available ??= All.Where(e => ResourceExists(e.Key)).ToList();
        }
    }

    private static readonly object AvailableLock = new();

    public static IEnumerable<PictureEntry> InCategory(AvatarCategory c) => Available.Where(e => e.AvatarGroup == c);

    public static string Label(string key) => PictureCatalog.Label(key);

    public static Uri ResourceUri(string key) =>
        new($"pack://application:,,,/Remindr;component/Assets/Pictures/{key}.png", UriKind.Absolute);

    private static bool ResourceExists(string key)
    {
        try
        {
            var info = Application.GetResourceStream(ResourceUri(key));
            if (info is null) return false;
            info.Stream.Dispose();
            return true;
        }
        catch (Exception) { return false; }
    }

    private static readonly Dictionary<string, ImageSource?> Cache = new();
    private static readonly Regex FileValue = new(@"^avatars/(\d+)\.png$", RegexOptions.Compiled);

    /// <summary>
    /// Returns the picture for a stored 'builtin:key' value (avatar or reminder icon), or null for null/unknown/unreadable.
    /// Cached per (key, width); never throws.
    /// </summary>
    public static ImageSource? LoadBuiltin(string? value, int decodePixelWidth = 0)
    {
        try
        {
            var entry = PictureCatalog.FromValue(value);
            if (entry is null) return null;
            var cacheKey = entry.Key + "@" + decodePixelWidth;
            lock (Cache)
            {
                if (Cache.TryGetValue(cacheKey, out var hit)) return hit;
                var img = Decode(ResourceUri(entry.Key), decodePixelWidth);
                Cache[cacheKey] = img;
                return img;
            }
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Returns the picture for a stored avatar value, or null (coloured initial) for null/unknown/missing/unreadable.
    /// Built-in images are cached; uploaded files are re-read every time so an edit shows up. Never throws.
    /// </summary>
    public static ImageSource? Load(string? avatar, string? avatarDirectory = null, int decodePixelWidth = 0)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(avatar)) return null;

            if (avatar.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
                return LoadBuiltin(avatar, decodePixelWidth);

            if (avatar.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                var m = FileValue.Match(avatar.Substring(FilePrefix.Length));
                if (!m.Success) return null;
                var path = Path.Combine(avatarDirectory ?? AvatarService.DefaultDirectory, m.Groups[1].Value + ".png");
                return File.Exists(path) ? Decode(new Uri(path), decodePixelWidth) : null;
            }
        }
        catch (Exception) { /* fall through to the initial */ }
        return null;
    }

    private static ImageSource? Decode(Uri uri, int decodePixelWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth; // decode at displayed size so it stays crisp
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }
}
