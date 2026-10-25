using System;
using System.Diagnostics;
using System.Reflection;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>Static facts about this setup build. Repo identity comes from the linked RepoInfo.cs.</summary>
public static class SetupInfo
{
    /// <summary>"0.3.0" from the assembly's informational version.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>"0.3" for headings.</summary>
    public static string ShortVersion
    {
        get
        {
            var v = VersionCompare.Parse(Version);
            return v == null ? Version : v.Major + "." + v.Minor;
        }
    }

    public static AppIdentity Identity { get; } =
        new AppIdentity(Version, RepoInfo.Publisher, RepoInfo.Aumid, RepoInfo.RepoUrl, RepoInfo.IssuesUrl);

    public static string ExePath { get; } = Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetEntryAssembly()!.Location;

    private static string ReadVersion()
    {
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info!.IndexOf('+');
        return plus >= 0 ? info.Substring(0, plus) : info;
    }
}
