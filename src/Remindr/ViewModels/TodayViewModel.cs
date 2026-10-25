using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

public partial class TodayViewModel : ObservableObject
{
    private readonly ProfileSession _session;
    private readonly ReminderDialogs _dialogs;

    public TodayViewModel(ProfileSession session, ReminderDialogs dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        session.CurrentProfileChanged += (_, _) => OnPropertyChanged(nameof(Greeting));
        // Strings built here (not bound through {l:Loc}) are recomputed when the language changes.
        Loc.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(DateCaption));
        };
    }

    /// <summary>"Good morning, Alex" (before 12:00), "Good afternoon" (before 18:00), otherwise "Good evening".</summary>
    public string Greeting => GreetingFor(DateTime.Now, _session.Current?.Name);

    public string DateCaption => DateTime.Now.ToString(Loc.Get("Fmt_FullDate"), Loc.Culture);

    /// <summary>Opens the new-reminder dialog (the full Today dashboard comes in Phase 5).</summary>
    [RelayCommand]
    private Task NewReminderAsync() => _dialogs.ShowEditAsync(null);

    public static string GreetingFor(DateTime now, string? name)
    {
        var part = now.Hour < 12 ? "Today_Greeting_Morning" : now.Hour < 18 ? "Today_Greeting_Afternoon" : "Today_Greeting_Evening";
        return string.IsNullOrWhiteSpace(name) ? Loc.Get(part) : Loc.Format(part + "Named", name);
    }
}
