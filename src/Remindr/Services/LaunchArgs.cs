using System.Text.Json;

namespace Remindr.Services;

/// <summary>
/// The command-line switches Remindr understands. Anything else is ignored (an older Run value, a future switch, the
/// arguments Windows adds). Pure, so it is unit tested.
/// </summary>
public sealed record LaunchArgs(bool Minimized, bool Welcome, bool ToastActivated, bool NoEffects = false)
{
    /// <summary>Debug builds only (see MotionPolicy): turns the visual-effects gate off so every effect is instant.</summary>
    public const string NoEffectsArg = "--no-effects";
    public const string MinimizedArg = "--minimized";
    public const string WelcomeArg = "--welcome";
    /// <summary>Added by the toast library when Windows starts the app for a toast click.</summary>
    public const string ToastActivatedArg = "-ToastActivated";

    public static LaunchArgs Parse(IEnumerable<string>? args)
    {
        bool minimized = false, welcome = false, toast = false, noEffects = false;
        foreach (var a in args ?? Array.Empty<string>())
        {
            if (string.Equals(a, MinimizedArg, StringComparison.OrdinalIgnoreCase)) minimized = true;
            else if (string.Equals(a, WelcomeArg, StringComparison.OrdinalIgnoreCase)) welcome = true;
            else if (string.Equals(a, NoEffectsArg, StringComparison.OrdinalIgnoreCase)) noEffects = true;
            else if (string.Equals(a, ToastActivatedArg, StringComparison.OrdinalIgnoreCase)) toast = true;
        }
        return new LaunchArgs(minimized, welcome, toast, noEffects);
    }
}

/// <summary>What a process that finds Remindr already running does.</summary>
public enum DuplicateLaunchAction
{
    /// <summary>Send the arguments to the running instance, then exit.</summary>
    ForwardAndExit,
    /// <summary>Exit without telling anyone (a toast click: the running instance already got it through the toast library).</summary>
    ExitSilently,
}

/// <summary>The rules for a second launch and the message it sends. Pure.</summary>
public static class SingleInstanceRules
{
    /// <summary>A toast-activated duplicate forwards nothing; every other duplicate forwards its arguments.</summary>
    public static DuplicateLaunchAction ForDuplicate(LaunchArgs second) =>
        second.ToastActivated ? DuplicateLaunchAction.ExitSilently : DuplicateLaunchAction.ForwardAndExit;

    /// <summary>The running instance brings its window forward for a second launch, unless that launch was itself --minimized.</summary>
    public static bool ShouldBringForward(LaunchArgs second) => !second.Minimized;

    private const int Version = 1;
    public const int MaxMessageBytes = 16 * 1024;

    private sealed record Message(int V, string[] Args);

    /// <summary>One line of JSON (no raw line breaks), so the pipe can frame it with a newline.</summary>
    public static string Encode(IEnumerable<string>? args) =>
        JsonSerializer.Serialize(new Message(Version, (args ?? Array.Empty<string>()).ToArray()));

    /// <summary>The arguments in a message, or null for anything that is not a valid version-1 message.</summary>
    public static string[]? Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxMessageBytes) return null;
        try
        {
            var m = JsonSerializer.Deserialize<Message>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return m is { V: Version, Args: not null } && m.Args.All(a => a is not null) ? m.Args : null;
        }
        catch (JsonException) { return null; }
    }
}
