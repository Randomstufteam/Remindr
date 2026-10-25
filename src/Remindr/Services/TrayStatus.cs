namespace Remindr.Services;

/// <summary>
/// What the tray icon says about the reminders of all profiles: how many are due today, and how many are overdue (open, past due, not
/// snoozed into the future). Overdue also covers what is being nagged, since a nag only repeats for an open reminder past its time.
/// </summary>
public sealed record TrayStatus(int DueToday, int Overdue)
{
    public static readonly TrayStatus None = new(0, 0);

    /// <summary>True when the icon carries its alert dot.</summary>
    public bool HasAlert => Overdue > 0;

    /// <summary>"Remindr" alone, or "Remindr &#183; 2 due today" (plural-aware, from the string table).</summary>
    public string Tooltip => DueToday > 0 ? Loc.Plural("Tray_TooltipDue", DueToday) : "Remindr";
}
