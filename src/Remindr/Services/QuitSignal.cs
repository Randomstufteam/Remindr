namespace Remindr.Services;

/// <summary>
/// A per-process named event the installer sets to ask this Remindr to exit for real. Closing the window alone is not
/// enough: with close-to-tray on, a close only hides it (and a hidden window has no main-window handle to close at all).
/// The name must match <c>ProcessGuard.QuitEventName</c> in the installer.
/// </summary>
public sealed class QuitSignal : IDisposable
{
    public static string NameFor(int processId) => $@"Local\Remindr.Quit.{processId}";

    private EventWaitHandle? _event;
    private RegisteredWaitHandle? _wait;

    /// <summary>Starts listening; <paramref name="onQuit"/> runs on a thread-pool thread (marshal to the UI yourself). Never throws.</summary>
    public void Start(Action onQuit)
    {
        try
        {
            _event = new EventWaitHandle(false, EventResetMode.ManualReset, NameFor(Environment.ProcessId));
            _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onQuit(), null, Timeout.Infinite, executeOnlyOnce: true);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Creating the quit signal"); }
    }

    public void Dispose()
    {
        try { _wait?.Unregister(null); _event?.Dispose(); } catch { /* shutting down */ }
        _wait = null; _event = null;
    }
}
