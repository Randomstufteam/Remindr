using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>The real HKCU registry. Only the sub keys setup owns may be touched.</summary>
public sealed class WinRegistryStore : IRegistryStore
{
    private static string Guard(string subKey)
    {
        var ok = subKey.StartsWith(InstallPaths.UninstallSubKey, StringComparison.OrdinalIgnoreCase)
                 || subKey.Equals(InstallPaths.RunSubKey, StringComparison.OrdinalIgnoreCase)
                 || subKey.Equals(InstallPaths.ToastClsidSubKey, StringComparison.OrdinalIgnoreCase)
                 || subKey.StartsWith(@"Software\Classes\AppUserModelId\" + RepoInfo.Aumid, StringComparison.OrdinalIgnoreCase);
        if (!ok) throw new InvalidOperationException("Refusing to touch registry key " + subKey);
        return subKey;
    }

    public bool KeyExists(string subKey)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Guard(subKey));
        return k != null;
    }

    public IDictionary<string, object>? ReadAll(string subKey)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Guard(subKey));
        if (k == null) return null;
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in k.GetValueNames())
        {
            var v = k.GetValue(name);
            if (v is string || v is int) d[name] = v;
        }
        return d;
    }

    public string? GetString(string subKey, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Guard(subKey));
        return k?.GetValue(name) as string;
    }

    public void SetValues(string subKey, IEnumerable<KeyValuePair<string, object>> values)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Guard(subKey));
        if (k == null) throw new InvalidOperationException("Could not create registry key " + subKey);
        foreach (var kv in values)
        {
            if (kv.Value is int i) k.SetValue(kv.Key, i, RegistryValueKind.DWord);
            else k.SetValue(kv.Key, (string)kv.Value, RegistryValueKind.String);
        }
    }

    public void DeleteValue(string subKey, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Guard(subKey), true);
        k?.DeleteValue(name, false);
    }

    public void DeleteKeyTree(string subKey) => Registry.CurrentUser.DeleteSubKeyTree(Guard(subKey), false);
}
