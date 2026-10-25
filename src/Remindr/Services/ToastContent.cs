using Microsoft.Toolkit.Uwp.Notifications;
using Remindr.Models;

namespace Remindr.Services;

/// <summary>
/// Builds the toast for an alert. Separate from <see cref="NotificationService"/> so the content (texts, scenario, attribution,
/// picture) is unit tested as XML without showing anything.
/// </summary>
public static class ToastContent
{
    public const string AttributionSeparator = " · ";

    /// <summary>"Important &#183; Overdue &#183; Sam": only the parts that apply, profile last; null when the toast is neither important nor overdue.</summary>
    public static string? Attribution(bool important, bool overdue, string? profileName)
    {
        if (!important && !overdue) return null;
        var parts = new List<string>();
        if (important) parts.Add(Loc.Get("Toast_AttrImportant"));
        if (overdue) parts.Add(Loc.Get("Toast_AttrOverdue"));
        if (!string.IsNullOrWhiteSpace(profileName)) parts.Add(profileName!.Trim());
        return string.Join(AttributionSeparator, parts);
    }

    /// <param name="when">The "when" line, e.g. "Today 3:00 PM".</param>
    /// <param name="profileName">The reminder's profile, shown in the attribution of important / overdue toasts.</param>
    /// <param name="logo">A local PNG for the round picture beside the text, or null for the app icon.</param>
    public static ToastContentBuilder Build(Reminder r, PlannedAlert alert, string when, string? profileName, Uri? logo)
    {
        var open = new ToastArgs(ToastAction.Open, r.Id, r.ProfileId, alert.Occurrence);
        var heading = Loc.Get("Toast_Heading_" + alert.Kind);
        if (alert.IsOverdue) heading = Loc.Format("Toast_HeadingOverdue", heading);

        var notes = (r.Notes ?? "").Trim();
        var body = notes.Length == 0 ? when : when + "\n" + (notes.Length > 160 ? notes[..160] + "…" : notes);

        var builder = new ToastContentBuilder()
            .AddToastActivationInfo(open.Encode(), ToastActivationType.Foreground);
        // An important reminder stays on screen until it is acted on (the Reminder scenario needs the buttons below).
        if (r.IsImportant) builder.SetToastScenario(ToastScenario.Reminder);
        if (logo is not null) builder.AddAppLogoOverride(logo, ToastGenericAppLogoCrop.Circle);
        builder
            .AddText(heading, AdaptiveTextStyle.Caption)
            .AddText(r.Title, AdaptiveTextStyle.Header)
            .AddText(body);
        if (Attribution(r.IsImportant, alert.IsOverdue, profileName) is { } attribution) builder.AddAttributionText(attribution);
        return builder
            .AddComboBox(ToastArgs.SnoozeInputId, ToastActionHandler.ChoiceId(SnoozeChoice.Minutes5),
                (ToastActionHandler.ChoiceId(SnoozeChoice.Minutes5), Loc.Get("Toast_Snooze5")),
                (ToastActionHandler.ChoiceId(SnoozeChoice.Hour1), Loc.Get("Toast_Snooze60")),
                (ToastActionHandler.ChoiceId(SnoozeChoice.Tomorrow), Loc.Get("Toast_SnoozeTomorrow")))
            .AddButton(Button("Toast_Complete", open.With(ToastAction.Complete)))
            .AddButton(Button("Toast_Snooze", open.With(ToastAction.Snooze)))
            .AddButton(Button("Toast_Dismiss", open.With(ToastAction.Dismiss)));
    }

    private static ToastButton Button(string key, ToastArgs args) =>
        new ToastButton(Loc.Get(key), args.Encode()) { ActivationType = ToastActivationType.Foreground };
}
