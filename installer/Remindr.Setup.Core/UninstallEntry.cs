using System;
using System.Collections.Generic;
using System.Globalization;

namespace Remindr.Setup.Core;

/// <summary>The values of HKCU\...\Uninstall\Remindr that make the app show up in Settings > Apps.</summary>
public static class UninstallEntry
{
    public static List<KeyValuePair<string, object>> Build(InstallPaths paths, AppIdentity app, long estimatedSizeKb, DateTime installDate)
    {
        var v = new List<KeyValuePair<string, object>>();
        void Add(string name, object value) => v.Add(new KeyValuePair<string, object>(name, value));

        Add("DisplayName", "Remindr");
        Add("DisplayVersion", app.Version);
        Add("Publisher", app.Publisher);
        Add("DisplayIcon", paths.ExePath);
        Add("InstallLocation", paths.InstallDir);
        Add("UninstallString", paths.UninstallString);
        Add("QuietUninstallString", paths.QuietUninstallString);
        Add("EstimatedSize", (int)Math.Min(int.MaxValue, Math.Max(0, estimatedSizeKb)));   // KB, REG_DWORD
        Add("InstallDate", installDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Add("URLInfoAbout", app.RepoUrl);
        Add("HelpLink", app.IssuesUrl);
        Add("NoModify", 1);
        Add("NoRepair", 1);
        return v;
    }
}
