using System.IO;

namespace Remindr.Services;

/// <summary>Appends crash details to error.log in the data folder (%AppData%\Remindr, or REMINDR_DATA_DIR). Never throws.</summary>
public static class ErrorLog
{
    private static readonly string FolderPath = AppPaths.Current.DataDir;

    public static readonly string FilePath = AppPaths.Current.ErrorLogFile;

    /// <summary>One timestamped line, for expected conditions (e.g. offline at the update check) that need no stack trace.</summary>
    public static void WriteLine(string text)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var line = text.Replace('\r', ' ').Replace('\n', ' ');
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public static void Write(Exception ex, string context)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var block = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}" +
                        $"{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(FilePath, block);
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
