using System.Reflection;

namespace Remindr.Services;

/// <summary>App version and shell helpers used by Settings.</summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var v = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(v)) return "0.3.0";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    public static string OsText => FeedbackLinks.OsText(Environment.OSVersion.Version);

    /// <summary>Opens a URL or folder with the shell. Never throws.</summary>
    public static void Open(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Open " + (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? target : "folder"));
        }
    }
}
