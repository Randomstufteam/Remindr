using Remindr.Models;

namespace Remindr.Services;

/// <summary>Where the scheduler sends alerts. The real one shows Windows toasts; tests use a fake that records them.</summary>
public interface IToastSink
{
    /// <summary>Shows the alert. Safe to call from any thread.</summary>
    Task ShowAsync(Reminder r, PlannedAlert alert, CancellationToken ct);
}
