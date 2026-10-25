using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Remindr.Data.Repositories;
using Remindr.Models;

namespace Remindr.Services;

/// <summary>
/// Windows toasts (Microsoft.Toolkit.Uwp.Notifications). Shows them, and routes clicks to <see cref="ToastActionHandler"/>
/// on the UI thread. Clicks arrive both while the app runs and when a click started the app (cold start): the library calls
/// OnActivated either way once <see cref="Initialize"/> has subscribed.
/// </summary>
public sealed class NotificationService
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    private readonly ToastActionHandler _handler;
    private readonly ReminderRepo _reminders;
    private readonly IClock _clock;
    private Dispatcher? _dispatcher;
    private Action<ToastOutcome>? _onHandled;

    public NotificationService(ToastActionHandler handler, ReminderRepo reminders, IClock clock)
    {
        _handler = handler;
        _reminders = reminders;
        _clock = clock;
    }

    /// <summary>
    /// Must run first thing at start-up, before any toast API: gives the process the installer's AUMID so the library
    /// registers (and activates) toasts under it instead of inventing one from the exe path.
    /// </summary>
    public static void RegisterAppId()
    {
        try { SetCurrentProcessExplicitAppUserModelID(RepoInfo.Aumid); }
        catch (Exception ex) { ErrorLog.Write(ex, "Setting the AUMID"); }
    }

    /// <summary>True when this process was started by a click on a toast.</summary>
    public static bool StartedFromToast
    {
        get
        {
            try { return ToastNotificationManagerCompat.WasCurrentProcessToastActivated(); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Subscribes to toast clicks and clears toasts of reminders that get completed or deleted.
    /// <paramref name="onHandled"/> runs on the UI thread after the click's effect was applied (also when the reminder was gone).
    /// </summary>
    public void Initialize(Dispatcher dispatcher, Action<ToastOutcome> onHandled)
    {
        _dispatcher = dispatcher;
        _onHandled = onHandled;
        _reminders.Closed += (_, r) => ClearHistory(r);
        try
        {
            ToastNotificationManagerCompat.OnActivated += e =>
            {
                // Library thread: copy what is needed and hop to the UI thread.
                var text = e.Argument;
                e.UserInput.TryGetValue(ToastArgs.SnoozeInputId, out var choice);
                dispatcher.BeginInvoke(() => HandleActivation(text, choice?.ToString()));
            };
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Subscribing to toast clicks"); }
    }

    private void HandleActivation(string? argumentText, string? snoozeChoice)
    {
        try
        {
            if (!ToastArgs.TryParse(argumentText, out var args))
            {
                // A click without our arguments (e.g. an old toast): just bring the app forward.
                _onHandled?.Invoke(new ToastOutcome(ToastAction.Open, false));
                return;
            }
            var outcome = _handler.Handle(args, snoozeChoice);
            if (outcome.Found) ClearToast(args);
            _onHandled?.Invoke(outcome);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Handling a toast click");
            _onHandled?.Invoke(new ToastOutcome(ToastAction.Open, false));
        }
    }

    /// <summary>Shows (or replaces) the toast for this reminder occurrence.</summary>
    public void Show(Reminder r, PlannedAlert alert)
    {
        try
        {
            var open = new ToastArgs(ToastAction.Open, r.Id, r.ProfileId, alert.Occurrence);
            var heading = Loc.Get("Toast_Heading_" + alert.Kind);
            if (alert.IsOverdue) heading = Loc.Format("Toast_HeadingOverdue", heading);

            var when = ReminderQueries.WhenText(r, _clock.Now, showDate: true);
            var notes = (r.Notes ?? "").Trim();
            var body = notes.Length == 0 ? when : when + "\n" + (notes.Length > 160 ? notes[..160] + "…" : notes);

            var builder = new ToastContentBuilder()
                .AddToastActivationInfo(open.Encode(), ToastActivationType.Foreground)
                .AddText(heading, AdaptiveTextStyle.Caption)
                .AddText(r.Title, AdaptiveTextStyle.Header)
                .AddText(body)
                .AddComboBox(ToastArgs.SnoozeInputId, ToastActionHandler.ChoiceId(SnoozeChoice.Minutes5),
                    (ToastActionHandler.ChoiceId(SnoozeChoice.Minutes5), Loc.Get("Toast_Snooze5")),
                    (ToastActionHandler.ChoiceId(SnoozeChoice.Hour1), Loc.Get("Toast_Snooze60")),
                    (ToastActionHandler.ChoiceId(SnoozeChoice.Tomorrow), Loc.Get("Toast_SnoozeTomorrow")))
                .AddButton(Button("Toast_Complete", open.With(ToastAction.Complete)))
                .AddButton(Button("Toast_Snooze", open.With(ToastAction.Snooze)))
                .AddButton(Button("Toast_Dismiss", open.With(ToastAction.Dismiss)));

            builder.Show(toast =>
            {
                toast.Tag = open.Tag;
                toast.Group = open.Group;
            });
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Showing a toast"); }
    }

    /// <summary>A sample toast for the Settings test button: the next upcoming reminder, else a sample one.</summary>
    public void ShowTest(Reminder? next)
    {
        var r = next ?? new Reminder
        {
            Id = 0, ProfileId = 0, Title = Loc.Get("Toast_TestTitle"), Notes = Loc.Get("Toast_TestNotes"),
            DueDate = _clock.Today, DueTime = TimeOnly.FromDateTime(_clock.Now)
        };
        Show(r, new PlannedAlert(NotificationKind.AtTime, NotificationPlanner.OccurrenceKey(r), false));
    }

    private static ToastButton Button(string key, ToastArgs args) =>
        new ToastButton(Loc.Get(key), args.Encode()) { ActivationType = ToastActivationType.Foreground };

    private static void ClearToast(ToastArgs a)
    {
        try { ToastNotificationManagerCompat.History.Remove(a.Tag, a.Group); } catch { /* nothing to clear */ }
    }

    private void ClearHistory(Reminder r)
    {
        var occ = NotificationPlanner.OccurrenceKey(r);
        void Clear()
        {
            try { ToastNotificationManagerCompat.History.Remove(ToastArgs.TagFor(r.ProfileId, r.Id), occ); }
            catch { /* nothing to clear */ }
        }
        if (_dispatcher is null || _dispatcher.CheckAccess()) Clear(); else _dispatcher.BeginInvoke(Clear);
    }
}
