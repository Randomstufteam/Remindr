namespace Remindr.Services;

/// <summary>Source of "now" so date logic can be tested with a fake clock.</summary>
public interface IClock
{
    /// <summary>Local wall-clock time.</summary>
    DateTime Now { get; }

    DateTime UtcNow { get; }

    DateOnly Today => DateOnly.FromDateTime(Now);
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
