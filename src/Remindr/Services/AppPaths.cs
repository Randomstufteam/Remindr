using System.IO;

namespace Remindr.Services;

/// <summary>
/// Where Remindr keeps its data: the database, the uploaded avatars and error.log. Normally <c>%AppData%\Remindr</c>.
/// The <c>REMINDR_DATA_DIR</c> environment variable moves all of it to another folder, so first run (and the screenshot tools) can be
/// tried on an empty folder without ever touching the real data. UI-free.
/// </summary>
public sealed class AppPaths
{
    public const string EnvironmentVariable = "REMINDR_DATA_DIR";

    private AppPaths(string dataDir)
    {
        DataDir = dataDir;
        DbFile = Path.Combine(dataDir, "remindr.db");
        AvatarsDir = Path.Combine(dataDir, "avatars");
        ErrorLogFile = Path.Combine(dataDir, "error.log");
        ToastCacheDir = Path.Combine(dataDir, "toast-cache");
    }

    /// <summary>The folder everything lives in.</summary>
    public string DataDir { get; }
    public string DbFile { get; }
    public string AvatarsDir { get; }
    public string ErrorLogFile { get; }
    /// <summary>Small circle-cropped pictures for toasts; unused ones are removed after a week (see <see cref="ToastImageCache"/>).</summary>
    public string ToastCacheDir { get; }

    /// <summary>True when the folder came from <see cref="EnvironmentVariable"/> instead of %AppData%.</summary>
    public bool IsOverridden { get; private init; }

    /// <summary>The paths for this process (the environment variable is read once, when first used).</summary>
    public static AppPaths Current { get; } = FromEnvironmentValue(Environment.GetEnvironmentVariable(EnvironmentVariable));

    /// <summary>
    /// Paths for an override value: null, empty or blank means the default %AppData%\Remindr; otherwise the (made absolute) folder.
    /// A value that is not a usable path falls back to the default rather than failing at start-up.
    /// </summary>
    public static AppPaths FromEnvironmentValue(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                return new AppPaths(Path.GetFullPath(value.Trim())) { IsOverridden = true };
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // fall through to the default
            }
        }
        return new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Remindr"));
    }
}
