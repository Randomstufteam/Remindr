namespace Remindr.Models;

public sealed class Reminder
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int? CategoryId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    /// <summary>Local calendar date (stored yyyy-MM-dd).</summary>
    public DateOnly DueDate { get; set; }
    /// <summary>Local time of day (stored HH:mm). Null = all-day.</summary>
    public TimeOnly? DueTime { get; set; }
    public bool IsImportant { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>UTC yyyy-MM-ddTHH:mm:ssZ, set while completed.</summary>
    public string? CompletedAt { get; set; }
    /// <summary>Optional picture: 'builtin:&lt;key&gt;' or null. An unknown key simply shows no picture.</summary>
    public string? Icon { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public bool IsAllDay => DueTime is null;

    public Reminder Clone() => (Reminder)MemberwiseClone();
}

