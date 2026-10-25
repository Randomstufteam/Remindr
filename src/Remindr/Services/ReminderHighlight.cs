using Remindr.Models;
using Remindr.ViewModels;

namespace Remindr.Services;

/// <summary>Decides which card scrolls into view and glows after a save.</summary>
public static class ReminderHighlight
{
    /// <summary>The id to highlight: the saved reminder, or null when the dialog was cancelled or nothing was stored.</summary>
    public static int? TargetId(Reminder? saved) => saved is { Id: > 0 } ? saved.Id : null;

    /// <summary>Asks every card of the saved reminder in <paramref name="visible"/> to glow. Returns how many were asked.</summary>
    public static int Apply(IEnumerable<ReminderItemViewModel> visible, Reminder? saved)
    {
        if (TargetId(saved) is not { } id) return 0;
        var n = 0;
        foreach (var item in visible)
            if (item.Id == id) { item.RequestGlow(); n++; }
        return n;
    }
}
