using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Services;
using Remindr.Services.Assistant;

namespace Remindr.ViewModels;

/// <summary>One entry of the voice combo: a Windows voice, or null for the Windows default.</summary>
public sealed record VoiceOption(string? Name, string Display);

/// <summary>One voice-model download row in Settings > Voice: download, pause, resume, cancel and remove.</summary>
public sealed partial class ModelRowViewModel : ObservableObject
{
    private readonly VoiceModelManager _model;

    public ModelRowViewModel(VoiceModelManager model, string titleKey, string? creditKey)
    {
        _model = model;
        TitleKey = titleKey;
        CreditKey = creditKey;
        // Both rows live as long as the app (singletons), so these handlers are never removed.
        model.Changed += (_, _) => VoiceViewModel.OnUi(() => OnPropertyChanged(string.Empty));
        Loc.CultureChanged += (_, _) => VoiceViewModel.OnUi(() => OnPropertyChanged(string.Empty));
    }

    public string TitleKey { get; }
    public string? CreditKey { get; }
    public string Title => Loc.Get(TitleKey);
    public string? Credit => CreditKey is null ? null : Loc.Get(CreditKey);
    public bool HasCredit => CreditKey is not null;
    public bool Installed => _model.Installed;
    public bool ShowProgress => _model.State is ModelDownloadState.Downloading or ModelDownloadState.Verifying or ModelDownloadState.Paused;
    public bool CanDownload => !_model.Installed && !ShowProgress;
    public bool IsDownloading => _model.State == ModelDownloadState.Downloading;
    public bool IsPaused => _model.State == ModelDownloadState.Paused;
    public double Progress => _model.Progress;
    public string ProgressText => _model.State == ModelDownloadState.Verifying ? Loc.Get("Assistant_Dl_Verifying")
        : Loc.Format(IsPaused ? "Assistant_Dl_Paused" : "Assistant_Dl_Progress", ModelPickerViewModel.Size(_model.BytesDone), ModelPickerViewModel.Size(_model.TotalBytes));
    public string? ErrorText => _model.ErrorKey is { } k ? Loc.Get(k) : null;
    public bool HasError => _model.ErrorKey is not null;

    [RelayCommand] private Task Download() => _model.StartAsync();
    [RelayCommand] private Task Resume() => _model.StartAsync();
    [RelayCommand] private void Pause() => _model.Pause();
    [RelayCommand] private void CancelDownload() => _model.Cancel();
    [RelayCommand] private void Remove() => _model.Remove();
}

/// <summary>Settings > Voice: the Voice input toggle, the voice-model download, Speak replies and the choice of voice.</summary>
public sealed partial class VoiceViewModel : ObservableObject
{
    private readonly VoiceSettings _settings;
    private readonly ISpeaker _speaker;
    private readonly VoiceModelManager _model;
    private readonly LfmAudioServer _server;
    private IReadOnlyList<VoiceOption>? _voices;

    public VoiceViewModel(VoiceSettings settings, VoiceModelManager model, ISpeaker speaker, LfmAudioServer server)
    {
        _settings = settings;
        _speaker = speaker;
        _model = model;
        _server = server;
        Hearing = new ModelRowViewModel(model, "Voice_Model_Name", "Voice_Model_Credit");
        model.InstalledChanged += (_, _) => OnUi(() => { if (!model.Installed) server.Stop(); _voices = null; OnPropertyChanged(nameof(Voices)); OnPropertyChanged(nameof(SelectedVoice)); });
        Loc.CultureChanged += (_, _) => OnUi(() => { _voices = null; OnPropertyChanged(string.Empty); });
    }

    internal static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(action);
        else action();
    }

    public ModelRowViewModel Hearing { get; }

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (_settings.Enabled == value) return;
            _settings.Enabled = value;
            if (!value) _server.Stop();
            OnPropertyChanged();
        }
    }

    public bool SpeakReplies
    {
        get => _settings.SpeakReplies;
        set { _settings.SpeakReplies = value; OnPropertyChanged(); }
    }

    /// <summary>The natural voices (once the voice model is downloaded), then the Windows voices.</summary>
    public IReadOnlyList<VoiceOption> Voices => _voices ??= new[] { new VoiceOption(null, Loc.Get(_model.Installed ? "Voice_DefaultNatural" : "Voice_DefaultVoice")) }
        .Concat(_model.Installed ? AudioModelCatalog.Voices.Select(v => new VoiceOption(AudioModelCatalog.Prefix + v.Id, Loc.Format("Voice_NaturalVoice", Loc.Get(v.NameKey))))
            : Array.Empty<VoiceOption>())
        .Concat(_speaker.Voices().Select(n => new VoiceOption(n, n))).ToList();

    /// <summary>The chosen voice's name (null = the default: the female natural voice once downloaded, else the Windows default).</summary>
    public string? SelectedVoice
    {
        get => _settings.VoiceName;
        set { _settings.VoiceName = value; OnPropertyChanged(); }
    }
}
