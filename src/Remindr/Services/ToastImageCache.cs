using System.IO;
using System.Text.RegularExpressions;
using Remindr.Models;

namespace Remindr.Services;

/// <summary>
/// The small circular pictures shown on toasts (<c>&lt;data dir&gt;\toast-cache\</c>). Toasts need a file path, not a resource, and a
/// toast in Action Center keeps pointing at its file, so start-up only removes pictures unused for a week. No UI types, so it is unit tested.
/// </summary>
public sealed class ToastImageCache
{
    /// <summary>Edge length of the PNGs in pixels (kept small: Windows may drop toast images over about 200 KB).</summary>
    public const int PixelSize = 96;

    private static readonly Regex Unsafe = new("[^A-Za-z0-9_-]", RegexOptions.Compiled);

    public ToastImageCache(string directory) => Directory = directory;

    public string Directory { get; }

    /// <summary>
    /// How long an unused picture is kept. Not "everything at start-up": a toast shown just before a restart (or the toast whose click
    /// cold-starts the app) still points at its file in Action Center, so only files nobody has used for a week are removed.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>
    /// Deletes pictures not used for <see cref="MaxAge"/> and half-written leftovers (called once at start-up). Best effort: a locked
    /// file is left for the next start.
    /// </summary>
    public void Clean() => Clean(DateTime.UtcNow);

    public void Clean(DateTime nowUtc)
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            foreach (var f in System.IO.Directory.EnumerateFiles(Directory))
            {
                try
                {
                    var stale = f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || nowUtc - File.GetLastWriteTimeUtc(f) > MaxAge;
                    if (stale) File.Delete(f);
                }
                catch { /* in use: the next start removes it */ }
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Cleaning the toast cache"); }
    }

    /// <summary>
    /// The path of the cached picture for <paramref name="key"/>, made by <paramref name="render"/> (PNG bytes) on first use.
    /// Null when there is nothing to draw or the file cannot be written; a toast then simply has no picture.
    /// </summary>
    public string? GetOrCreate(string key, Func<byte[]?> render)
    {
        try
        {
            var path = Path.Combine(Directory, FileName(key));
            if (File.Exists(path))
            {
                // Reused: mark it fresh so the start-up clean keeps it while toasts may still show it.
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { /* locked: harmless */ }
                return path;
            }
            var bytes = render();
            if (bytes is null || bytes.Length == 0) return null;
            System.IO.Directory.CreateDirectory(Directory);
            // Write beside, then move: a toast never reads a half-written file.
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Making a toast picture");
            return null;
        }
    }

    public static string FileName(string key) => Unsafe.Replace(key, "_") + "-" + PixelSize + ".png";

    /// <summary>The picture a toast for <paramref name="r"/> shows: the reminder's own picture, else the profile's avatar, else none.</summary>
    public static string? PickPicture(Reminder r, Profile? profile)
    {
        if (!string.IsNullOrWhiteSpace(r.Icon) && PictureCatalog.FromValue(r.Icon) is not null) return r.Icon;
        return string.IsNullOrWhiteSpace(profile?.Avatar) ? null : profile!.Avatar;
    }

    /// <summary>
    /// Cache key for a picture value. A built-in picture is fixed; an uploaded avatar is a file that is overwritten on re-upload,
    /// so its last-write time is part of the key.
    /// </summary>
    public static string KeyFor(string value, string avatarsDirectory)
    {
        if (value.StartsWith(AvatarCatalog.FilePrefix, StringComparison.Ordinal))
        {
            var name = Path.GetFileName(value.Replace('/', Path.DirectorySeparatorChar));
            var path = Path.Combine(avatarsDirectory, name);
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
            return value + "-" + stamp;
        }
        return value;
    }
}
