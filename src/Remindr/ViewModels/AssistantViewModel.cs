using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Services.Assistant;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>What the user typed.</summary>
public sealed record UserBubble(string Text);

/// <summary>A plain answer, a question back, or the result line of a confirmed card.</summary>
public sealed record ReplyBubble(string Text);

/// <summary>The answer to a question: matching reminders as compact cards (click one to edit it).</summary>
public sealed record FoundBubble(string Heading, IReadOnlyList<ReminderItemViewModel> Cards);

/// <summary>A Chat reply that is still arriving: the text grows token by token.</summary>
public sealed partial class StreamBubble : ObservableObject
{
    [ObservableProperty] private string _text = "";
}

/// <summary>
/// Chat mode found that the message asks for a change: a "Do this in Work" chip instead of a reply. Tapping it switches to Work and shows
/// the confirm card for the call the guard already made. Chat itself has no path that writes reminders.
/// </summary>
public sealed partial class WorkChip : ObservableObject
{
    public WorkChip(string json, Action<WorkChip> run)
    {
        Json = json;
        RunCommand = new RelayCommand(() => run(this), () => IsPending);
    }

    public string Json { get; }
    public string Label => Loc.Get("Assistant_DoInWork");
    public RelayCommand RunCommand { get; }
    [ObservableProperty] private bool _isPending = true;
    partial void OnIsPendingChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
}

/// <summary>One line of a confirm card.</summary>
public sealed record ConfirmRow(string Kind, string Title, string Detail, bool Important);

/// <summary>The confirm card: every change listed, nothing written until Confirm. It collapses once answered.</summary>
public sealed partial class ConfirmBubble : ObservableObject
{
    /// <summary>The question read aloud for this card ("Add 'call mom' Friday at 6 PM?").</summary>
    public string Spoken { get; init; } = "";

    public ConfirmBubble(IReadOnlyList<ConfirmRow> rows, bool isDelete, Action<ConfirmBubble> confirm, Action<ConfirmBubble> cancel)
    {
        Rows = rows;
        IsDelete = isDelete;
        ConfirmCommand = new RelayCommand(() => confirm(this), () => IsPending);
        CancelCommand = new RelayCommand(() => cancel(this), () => IsPending);
    }

    public IReadOnlyList<ConfirmRow> Rows { get; }
    /// <summary>A delete card uses the danger style.</summary>
    public bool IsDelete { get; }
    public string ConfirmLabel => Loc.Get(IsDelete ? "Common_Delete" : "Assistant_Confirm");
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    [ObservableProperty] private bool _isPending = true;

    partial void OnIsPendingChanged(bool value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>One candidate of a "which one?" choice: the compact card, and the command that picks it.</summary>
public sealed record ChoiceItem(ReminderItemViewModel Card, IRelayCommand PickCommand);

/// <summary>Several reminders fit equally well: up to four compact cards plus Cancel. Picking one goes on to the confirm card.</summary>
public sealed partial class ChoiceBubble : ObservableObject
{
    public ChoiceBubble(string prompt, Action<ChoiceBubble> cancel)
    {
        Prompt = prompt;
        CancelCommand = new RelayCommand(() => cancel(this));
    }

    public string Prompt { get; }
    public List<ChoiceItem> Choices { get; } = new();
    public RelayCommand CancelCommand { get; }

    [ObservableProperty] private bool _isPending = true;
}

/// <summary>The Assistant page: the Work / Chat switch, the message list, the text box, the model state and the confirm cards.</summary>
public sealed partial class AssistantViewModel : ObservableObject
{
    private readonly AssistantService _assistant;
    private readonly AssistantPlanner _planner;
    private readonly ReminderRepo _repo;
    private readonly CategoryRepo _categories;
    private readonly ProfileSession _session;
    private readonly ReminderDialogs _dialogs;
    private readonly IClock _clock;
    private readonly SettingsRepo _settings;
    private readonly List<ChatTurn> _history = new();
    private AssistantMode _mode;
    private readonly PushToTalkController? _ptt;
    private readonly ISpeaker? _speaker;
    private readonly VoiceSettings? _voice;
    private readonly VoiceModelManager? _voiceModel;
    private CancellationTokenSource? _speakCts;
    private bool _isSpeaking;
    // Cancelled on a profile switch: an answer still on its way must not show up (or be confirmed) in the other profile.
    private CancellationTokenSource _askCts = new();

    public AssistantViewModel(AssistantService assistant, ReminderRepo repo, CategoryRepo categories, ProfileSession session,
        ReminderDialogs dialogs, IClock clock, SettingsRepo settings, ModelPickerViewModel modelPicker,
        PushToTalkController? ptt = null, ISpeaker? speaker = null, VoiceSettings? voice = null, VoiceModelManager? voiceModel = null)
    {
        _ptt = ptt;
        _speaker = speaker;
        _voice = voice;
        _voiceModel = voiceModel;
        if (ptt is not null)
        {
            ptt.StateChanged += (_, _) => OnUi(OnPttState);
            ptt.Level += level => OnUi(() => { if (Orb == OrbState.Listening) OrbLevel = OrbModel.Smooth(OrbLevel, level); });
            ptt.Transcribed += text => OnUi(() => OnTranscribed(text));
            ptt.Failed += kind => OnUi(() => OnVoiceFailed(kind));
        }
        if (voice is not null) voice.Changed += (_, _) => OnUi(() => { UpdateOrb(); if (!voice.Enabled) StopVoice(); });
        if (voiceModel is not null) voiceModel.Changed += (_, _) => OnUi(UpdateOrb);
        UpdateOrb();
        _settings = settings;
        Models = modelPicker;
        modelPicker.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(ShowPicker)); };
        _assistant = assistant;
        _repo = repo;
        _categories = categories;
        _session = session;
        _dialogs = dialogs;
        _clock = clock;
        _planner = new AssistantPlanner(repo, clock);
        assistant.StateChanged += (_, _) => OnUi(RefreshState);
        // A card from another profile must never be confirmed in this one.
        session.CurrentProfileChanged += (_, _) => OnUi(() =>
        {
            _askCts.Cancel();
            _askCts = new CancellationTokenSource();
            StopVoice(); Messages.Clear(); _history.Clear(); LoadMode();
        });
        Loc.CultureChanged += (_, _) => OnUi(RefreshState);
        LoadMode();
    }

    public ObservableCollection<object> Messages { get; } = new();

    /// <summary>The model picker, shown here while no model is installed.</summary>
    public ModelPickerViewModel Models { get; }
    public bool ShowPicker => IsNotInstalled || Models.ShowProgress;

    /// <summary>Raised when a streamed reply grew (so the page can keep it in view).</summary>
    public event EventHandler? ContentGrew;

    /// <summary>Work (default) or Chat, remembered per profile (<c>assistant_mode</c>).</summary>
    public AssistantMode Mode
    {
        get => _mode;
        private set
        {
            if (_mode == value) return;
            _mode = value;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsWork));
            OnPropertyChanged(nameof(IsChat));
        }
    }

    public bool IsWork { get => Mode == AssistantMode.Work; set { if (value) SetMode(AssistantMode.Work); } }
    public bool IsChat { get => Mode == AssistantMode.Chat; set { if (value) SetMode(AssistantMode.Chat); } }

    private void SetMode(AssistantMode mode)
    {
        Mode = mode;
        if (_session.Current is { } p) AssistantModes.Write(_settings, p.Id, mode);
    }

    private void LoadMode() => Mode = _session.Current is { } p ? AssistantModes.Read(_settings, p.Id) : AssistantMode.Work;

    /// <summary>"Running on: NVIDIA ..." or "Running on: CPU" once the model is loaded.</summary>
    public string RunningOn => _assistant.IsLoaded ? Loc.Format("Assistant_RunningOn", _assistant.Device ?? "CPU") : "";
    public string GpuNote => _assistant.GpuNoteKey is { } k ? Loc.Get(k) : "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand))] private string _input = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCommand))] private bool _isBusy;
    partial void OnIsBusyChanged(bool value) => UpdateOrb();

    public AssistantState State => _assistant.State;
    public bool IsNotInstalled => State == AssistantState.NotInstalled;
    public bool IsWarming => State == AssistantState.Warming;
    public bool IsFailed => State == AssistantState.Failed;
    /// <summary>The text box is usable (the model may still be warming up: a message waits for it).</summary>
    public bool CanType => State is AssistantState.Ready or AssistantState.Warming;
    public string StateTitle => State switch
    {
        AssistantState.NotInstalled => Loc.Get("Assistant_State_NotInstalled"),
        AssistantState.Warming => Loc.Get("Assistant_State_Warming"),
        AssistantState.Failed => Loc.Get("Assistant_State_Failed"),
        _ => "",
    };
    public string StateDetail => State switch
    {
        AssistantState.NotInstalled => Loc.Get("Assistant_State_NotInstalledDetail"),
        AssistantState.Warming => Loc.Get("Assistant_State_WarmingDetail"),
        AssistantState.Failed => _assistant.FailureMessage ?? "",
        _ => "",
    };
    public bool HasState => State is AssistantState.NotInstalled or AssistantState.Warming or AssistantState.Failed;

    private void RefreshState()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsNotInstalled));
        OnPropertyChanged(nameof(IsWarming));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(CanType));
        OnPropertyChanged(nameof(StateTitle));
        OnPropertyChanged(nameof(StateDetail));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(ShowPicker));
        OnPropertyChanged(nameof(RunningOn));
        OnPropertyChanged(nameof(GpuNote));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The page was opened: start loading the model in the background (a no-op when it is off, missing or loaded).</summary>
    public void OnShown()
    {
        LoadMode();
        RefreshState();
        _ = WarmUpAsync();
    }

    private async Task WarmUpAsync()
    {
        try { await _assistant.EnsureReadyAsync(); }
        catch (Exception ex) { ErrorLog.Write(ex, "Assistant warm-up"); }
        OnUi(RefreshState);
    }

    private static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(action);
        else action();
    }

    private bool CanSend() => !IsBusy && CanType && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        Input = "";
        Messages.Add(new UserBubble(text));
        var first = Messages.Count;
        IsBusy = true;
        var ct = _askCts.Token;
        try
        {
            if (Mode == AssistantMode.Chat) await ChatAsync(text, ct);
            else await WorkAsync(text, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Assistant");
            Messages.Add(new ReplyBubble(Loc.Get("Assistant_Error")));
        }
        finally { IsBusy = false; }
        SpeakFrom(first);
    }

    /// <summary>Work: the tool pipeline (model, guard, planner). Chit-chat gets a pointer to Chat instead of a reply.</summary>
    private async Task WorkAsync(string text, CancellationToken ct)
    {
        var json = await _assistant.AskAsync(text, ct);
        ct.ThrowIfCancellationRequested();
        RefreshState();
        if (json is null) { Messages.Add(new ReplyBubble(StateTitle)); return; }
        if (AssistantModes.ToolOf(json) == "none") { Messages.Add(new ReplyBubble(Loc.Get("Assistant_SwitchToChat"))); return; }
        Show(_planner.Plan(json));
    }

    /// <summary>
    /// Chat: a plain reply streamed into the page. A message that looks like a request is first checked with the Work pipeline; if it is
    /// one, a "Do this in Work" chip replaces the reply. Nothing here writes reminders.
    /// </summary>
    private async Task ChatAsync(string text, CancellationToken ct)
    {
        if (AssistantModes.LooksLikeAction(text))
        {
            var json = await _assistant.AskAsync(text, ct);
            ct.ThrowIfCancellationRequested();
            RefreshState();
            if (json is null) { Messages.Add(new ReplyBubble(StateTitle)); return; }
            if (AssistantModes.IsAction(json)) { Messages.Add(new WorkChip(json, DoInWork)); return; }
        }
        _history.Add(new ChatTurn(true, text));
        var bubble = new StreamBubble();
        Messages.Add(bubble);
        await foreach (var token in _assistant.ChatAsync(AssistantModes.Recent(_history), ct))
        {
            bubble.Text += token;
            ContentGrew?.Invoke(this, EventArgs.Empty);
        }
        ct.ThrowIfCancellationRequested();
        bubble.Text = bubble.Text.Trim();
        if (bubble.Text.Length == 0) { bubble.Text = StateTitle; return; }
        _history.Add(new ChatTurn(false, bubble.Text));
    }

    private void DoInWork(WorkChip chip)
    {
        chip.IsPending = false;
        SetMode(AssistantMode.Work);
        try { Show(_planner.Plan(chip.Json)); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Assistant");
            Messages.Add(new ReplyBubble(Loc.Get("Assistant_Error")));
        }
    }

    private void Show(AssistantOutcome outcome)
    {
        switch (outcome)
        {
            case ReplyOutcome r:
                Messages.Add(new ReplyBubble(r.Text));
                break;
            case FoundOutcome f:
                Messages.Add(new FoundBubble(f.Heading, Cards(f.Reminders)));
                break;
            case ConfirmOutcome c:
                var now = _clock.Now;
                var rows = c.Changes.Select(ch => Row(ch, now)).ToList();
                var spoken = VoiceText.Confirm(c.Changes.Select((ch, i) => new VoiceText.Line(ch.Kind, rows[i].Title, rows[i].Detail, rows[i].Important)).ToList());
                Messages.Add(new ConfirmBubble(rows, c.IsDelete, b => Confirm(b, c), Cancel) { Spoken = spoken });
                break;
            case ChooseOutcome ch:
                var choice = new ChoiceBubble(ch.Prompt, Cancel);
                var cards = Cards(ch.Candidates);
                for (var i = 0; i < cards.Count; i++)
                {
                    var reminder = ch.Candidates[i];
                    choice.Choices.Add(new ChoiceItem(cards[i], new RelayCommand(() =>
                    {
                        // One pick per question (a double click must not open two cards).
                        if (!choice.IsPending) return;
                        choice.IsPending = false;
                        Show(ch.Pick(reminder));
                    })));
                }
                Messages.Add(choice);
                break;
        }
    }

    private void Confirm(ConfirmBubble bubble, ConfirmOutcome outcome)
    {
        bubble.IsPending = false;
        var first = Messages.Count;
        try
        {
            foreach (var line in _planner.Apply(outcome.Changes)) Messages.Add(new ReplyBubble(line));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Assistant apply");
            Messages.Add(new ReplyBubble(Loc.Get("Assistant_Error")));
        }
        SpeakFrom(first);
    }

    private void Cancel(ConfirmBubble bubble)
    {
        bubble.IsPending = false;
        var first = Messages.Count;
        Messages.Add(new ReplyBubble(Loc.Get("Assistant_Cancelled")));
        SpeakFrom(first);
    }
    private void Cancel(ChoiceBubble bubble) { bubble.IsPending = false; Messages.Add(new ReplyBubble(Loc.Get("Assistant_Cancelled"))); }

    private ConfirmRow Row(PlannedChange c, DateTime now)
    {
        var shown = c.After ?? c.Target!;
        var detail = c.Kind == ChangeKind.Snooze
            ? c.Snooze!.Tomorrow ? Loc.Get("Assistant_SnoozeTomorrow") : Loc.Format("Assistant_SnoozeUntil", c.Snooze.Until!.Value.ToString("t", Loc.Culture))
            : ReminderQueries.WhenText(shown, now, true);
        return new ConfirmRow(Loc.Get("Assistant_Kind_" + c.Kind), shown.Title, detail, shown.IsImportant && c.Kind is ChangeKind.Create or ChangeKind.Update);
    }

    private List<ReminderItemViewModel> Cards(IEnumerable<Reminder> reminders)
    {
        var now = _clock.Now;
        var byId = _session.Current is { } p ? _categories.ListByProfile(p.Id).ToDictionary(c => c.Id) : new Dictionary<int, Category>();
        // Edit and delete go through the same dialogs as everywhere; the list itself needs no refresh.
        var actions = new ReminderActionHandler(_repo, _dialogs, () => { });
        return reminders.Select(r => new ReminderItemViewModel(r, r.CategoryId is { } id && byId.TryGetValue(id, out var c) ? c : null,
            ReminderQueries.WhenText(r, now, true), true, actions, compact: true)).ToList();
    }

    // ---- Voice (slices 7-9): the orb, push-to-talk and spoken replies --------------------------------------------------------

    [ObservableProperty] private OrbState _orb = OrbState.Muted;
    /// <summary>0..1, smoothed; the mic level while listening, the speech level while speaking.</summary>
    [ObservableProperty] private double _orbLevel;
    /// <summary>The live line under the messages: "Listening...", the transcript, or a microphone problem.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasVoiceStatus))] private string _voiceStatus = "";
    [ObservableProperty] private bool _showMicLink;

    public bool HasVoiceStatus => VoiceStatus.Length > 0;
    /// <summary>Set by the page while it is on screen: the Ctrl+Space hotkey works only then.</summary>
    public bool IsShown { get; set; }

    private bool VoiceOn => _voice?.Enabled == true;

    /// <summary>The hotkey may start recording: voice is on and its model is installed, the assistant page is open.</summary>
    public bool CanHold => IsShown && _ptt is not null && VoiceOn && _voiceModel?.Installed == true;

    private void UpdateOrb()
    {
        var listening = _ptt?.State == PttState.Recording;
        var thinking = IsBusy || _ptt?.State == PttState.Transcribing;
        Orb = OrbModel.Resolve(VoiceOn, listening, _isSpeaking, thinking);
        if (Orb is not (OrbState.Listening or OrbState.Speaking)) OrbLevel = 0;
    }

    private void OnPttState()
    {
        VoiceStatus = _ptt!.State switch
        {
            PttState.Recording => Loc.Get("Voice_Status_Listening"),
            PttState.Transcribing => Loc.Get("Voice_Status_Transcribing"),
            _ => VoiceStatus,
        };
        if (_ptt.State != PttState.Idle) ShowMicLink = false;
        UpdateOrb();
    }

    private void OnVoiceFailed(VoiceError kind)
    {
        VoiceStatus = Loc.Get(kind switch
        {
            VoiceError.NoMicrophone => "Voice_Err_NoMic",
            VoiceError.AccessDenied => "Voice_Err_Denied",
            VoiceError.NothingHeard => "Voice_Err_Nothing",
            _ => "Voice_Err_Failed",
        });
        ShowMicLink = kind == VoiceError.AccessDenied;
        UpdateOrb();
    }

    /// <summary>The orb / mic button: toggles recording.</summary>
    [RelayCommand]
    private void ToggleMic()
    {
        if (_ptt is null || !CheckVoiceReady()) return;
        StopSpeaking();
        _ptt.Toggle();
    }

    private bool CheckVoiceReady()
    {
        if (!VoiceOn) VoiceStatus = Loc.Get("Voice_Status_Off");
        else if (_voiceModel?.Installed != true) VoiceStatus = Loc.Get("Voice_Status_NoModel");
        else return true;
        ShowMicLink = false;
        return false;
    }

    /// <summary>Ctrl+Space pressed (key repeat is ignored while recording).</summary>
    public void BeginHold()
    {
        if (!CanHold) return;
        StopSpeaking();
        _ptt!.Begin(PttSource.Key);
    }

    /// <summary>Ctrl+Space released: transcribe.</summary>
    public void EndHold() => _ptt?.End(PttSource.Key);

    /// <summary>Esc or the window lost focus: stop speaking and drop a recording.</summary>
    public void StopVoice()
    {
        StopSpeaking();
        _ptt?.Cancel();
    }

    [RelayCommand]
    private void OpenMicSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); }
        catch (Exception ex) { ErrorLog.Write(ex, "Open microphone settings"); }
    }

    /// <summary>A transcript: "yes"/"confirm"/"do it" or "no"/"cancel" answer an open confirm card; anything else is sent like typed text.</summary>
    private void OnTranscribed(string text)
    {
        VoiceStatus = Loc.Format("Voice_Status_Heard", text);
        ShowMicLink = false;
        StopSpeaking();
        var answer = VoiceText.Answer(text);
        // Only the card the conversation ends on: a "yes" must never confirm an older card the user has moved past.
        if (answer != SpokenAnswer.None && Messages.LastOrDefault(m => m is UserBubble or ConfirmBubble or ChoiceBubble) is ConfirmBubble { IsPending: true } card)
        {
            Messages.Add(new UserBubble(text));
            (answer == SpokenAnswer.Yes ? card.ConfirmCommand : card.CancelCommand).Execute(null);
            return;
        }
        Input = text;
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    private void StopSpeaking() => _speakCts?.Cancel();

    /// <summary>Reads the short replies (and a new confirm card's question) added since <paramref name="first"/> aloud, if Speak replies is on.</summary>
    private void SpeakFrom(int first)
    {
        if (_speaker is null || _voice is not { Enabled: true, SpeakReplies: true }) return;
        var parts = new List<string>();
        for (var i = first; i < Messages.Count; i++)
        {
            switch (Messages[i])
            {
                case ReplyBubble r: parts.Add(r.Text); break;
                case FoundBubble f: parts.Add(f.Heading); break;
                case ChoiceBubble { IsPending: true } c: parts.Add(c.Prompt); break;
                case ConfirmBubble { IsPending: true } c: parts.Add(c.Spoken); break;
                case StreamBubble s: parts.Add(s.Text); break;
            }
        }
        if (VoiceText.Short(string.Join(" ", parts.Where(p => p.Length > 0))) is { } text) _ = SpeakAsync(text);
    }

    private async Task SpeakAsync(string text)
    {
        StopSpeaking();
        var cts = _speakCts = new CancellationTokenSource();
        _isSpeaking = true;
        UpdateOrb();
        try { await _speaker!.SpeakAsync(text, _voice!.VoiceName, level => OnUi(() => { if (Orb == OrbState.Speaking) OrbLevel = OrbModel.Smooth(OrbLevel, level); }), cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorLog.Write(ex, "Spoken reply"); }
        finally
        {
            // A newer reply may already be speaking: only the latest one clears the state.
            if (_speakCts == cts) { _speakCts = null; _isSpeaking = false; UpdateOrb(); }
            cts.Dispose();
        }
    }
}
