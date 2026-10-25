namespace Remindr.Services;

/// <summary>
/// Lets Settings (and the <c>--welcome</c> start-up argument) ask the shell to show the welcome greeting. UI-free: the shell
/// subscribes to <see cref="ReplayRequested"/> and shows the greeting only. Replaying changes no data.
/// </summary>
public sealed class WelcomeController
{
    /// <summary>True when the app was started with <c>--welcome</c>: the greeting shows once at start-up, then the normal flow continues.</summary>
    public bool ShowOnStart { get; set; }

    public event EventHandler? ReplayRequested;

    public void Replay() => ReplayRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>True when <paramref name="args"/> contains the debug switch <c>--welcome</c>.</summary>
    public static bool HasWelcomeArg(IEnumerable<string>? args) =>
        args?.Any(a => string.Equals(a, "--welcome", StringComparison.OrdinalIgnoreCase)) == true;
}
