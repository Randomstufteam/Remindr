using Microsoft.Win32;
using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>The per-user start-up registry values, behind an interface so tests use a fake.</summary>
public interface IStartupRegistry
{
    /// <summary>HKCU\...\CurrentVersion\Run value, or null.</summary>
    string? GetRun(string name);
    void SetRun(string name, string command);
    void DeleteRun(string name);
    /// <summary>HKCU\...\Explorer\StartupApproved\Run value (what Task Manager's Startup apps tab writes), or null.</summary>
    byte[]? GetApproved(string name);
    void DeleteApproved(string name);
}

public sealed class WindowsStartupRegistry : IStartupRegistry
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? GetRun(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void SetRun(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteRun(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetApproved(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(name) as byte[];
    }

    public void DeleteApproved(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// "Start Remindr when I sign in": the HKCU Run value <c>Remindr</c> = <c>"exe" --minimized</c>. The registry is the truth;
/// the global app_setting <c>start_with_windows</c> only mirrors it. A value that Task Manager's Startup apps tab disabled counts as off.
/// </summary>
public sealed class StartWithWindowsService
{
    public const string SettingKey = "start_with_windows";
    public const string ValueName = "Remindr";

    private readonly IStartupRegistry _registry;
    private readonly SettingsRepo _settings;
    private readonly Func<string?> _exePath;

    public StartWithWindowsService(IStartupRegistry registry, SettingsRepo settings)
        : this(registry, settings, () => Environment.ProcessPath) { }

    public StartWithWindowsService(IStartupRegistry registry, SettingsRepo settings, Func<string?> exePath)
    {
        _registry = registry;
        _settings = settings;
        _exePath = exePath;
    }

    /// <summary>"C:\...\Remindr.exe" --minimized (same format as the installer writes).</summary>
    public static string CommandFor(string exePath) => "\"" + exePath + "\" " + LaunchArgs.MinimizedArg;

    /// <summary>Task Manager marks a disabled entry with an odd first byte (03); an enabled one is 02 or 06.</summary>
    public static bool IsDisabledByWindows(byte[]? approved) => approved is { Length: > 0 } && (approved[0] & 1) == 1;

    /// <summary>Reads the real state (never throws), and keeps the app_setting mirror in step with it.</summary>
    public bool Read()
    {
        var on = false;
        try
        {
            on = !string.IsNullOrWhiteSpace(_registry.GetRun(ValueName)) && !IsDisabledByWindows(_registry.GetApproved(ValueName));
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Reading the start-with-Windows value"); }
        Mirror(on);
        return on;
    }

    /// <summary>Turns it on or off. Returns the resulting real state (so a failed write shows as unchanged).</summary>
    public bool Set(bool on)
    {
        try
        {
            if (on)
            {
                var exe = _exePath();
                if (string.IsNullOrEmpty(exe)) return Read();
                _registry.SetRun(ValueName, CommandFor(exe));
                // If the user had switched it off in Task Manager, turning it on here must really turn it on.
                _registry.DeleteApproved(ValueName);
            }
            else
            {
                _registry.DeleteRun(ValueName);
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Saving the start-with-Windows value"); }
        return Read();
    }

    private void Mirror(bool on)
    {
        try { _settings.Set(SettingKey, on ? "1" : "0"); }
        catch (Exception ex) { ErrorLog.Write(ex, "Saving start_with_windows"); }
    }
}
