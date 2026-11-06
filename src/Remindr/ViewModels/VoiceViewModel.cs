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
        : Loc.Format(IsPaused ? "Assistant_Dl_Paused" : "Assistant_Dl_Progress", ModelPickerViewModel.Size(_model.BytesDone), ModelPickerViewModel.Size(_model.Tier.Bytes));
    public string? ErrorText => _model.ErrorKey is { } k ? Loc.Get(k) : null;
    public bool HasError => _model.ErrorKey is not null;

    [RelayCommand] private Task Download() => _model.StartAsync();
    [RelayCommand] private Task Resume() => _model.StartAsync();
    [RelayCommand] private void Pause() => _model.Pause();
    [RelayCommand] private void CancelDownload() => _model.Cancel();
    [RelayCommand] private void Remove() => _model.Remove();
}

/// <summary>Settings > Voice: the Voice input toggle, the speech-model download, Speak replies, the natural-voice downloads and the choice of voice.</summary>
public sealed partial class VoiceViewModel : ObservableObject
{
    private readonly VoiceSettings _settings;
    private readonly ISpeaker _speaker;
    private readonly WhisperTranscriber _transcriber;
    private readonly NaturalVoiceModels _natural;
    private IReadOnlyList<VoiceOption>? _voices;

    public VoiceViewModel(VoiceSettings settings, VoiceModelManager model, ISpeaker speaker, WhisperTranscriber transcriber, NaturalVoiceModels natural)
    {
        _settings = settings;
        _speaker = speaker;
        _transcriber = transcriber;
        _natural = natural;
        Hearing = new ModelRowViewModel(model, "Voice_Model_Name", "Voice_Model_Credit");
        NaturalOriginal = new ModelRowViewModel(natural.Original, "Voice_Natural_Original", null);
        NaturalSmall = new ModelRowViewModel(natural.Small, "Voice_Natural_Small", null);
        // A removed model must not stay in memory.
        model.InstalledChanged += (_, _) => { if (!model.Installed) transcriber.Unload(); };
        natural.InstalledChanged += (_, _) => OnUi(() => { _voices = null; OnPropertyChanged(nameof(Voices)); OnPropertyChanged(nameof(SelectedVoice)); });
        Loc.CultureChanged += (_, _) => OnUi(() => { _voices = null; OnPropertyChanged(string.Empty); });
    }

    internal static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(action);
        else action();
    }

    public ModelRowViewModel Hearing { get; }
    public ModelRowViewModel NaturalOriginal { get; }
    public ModelRowViewModel NaturalSmall { get; }

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (_settings.Enabled == value) return;
            _settings.Enabled = value;
            if (!value) { _transcriber.Unload(); _natural.Speaker?.Unload(); }
            OnPropertyChanged();
        }
    }

    public bool SpeakReplies
    {
        get => _settings.SpeakReplies;
        set { _settings.SpeakReplies = value; if (!value) _natural.Speaker?.Unload(); OnPropertyChanged(); }
    }

    /// <summary>The natural voices (once a Kokoro size is downloaded), then the Windows voices.</summary>
    public IReadOnlyList<VoiceOption> Voices => _voices ??= new[] { new VoiceOption(null, Loc.Get(_natural.InstalledPath is null ? "Voice_DefaultVoice" : "Voice_DefaultNatural")) }
        .Concat(_natural.InstalledPath is null ? Array.Empty<VoiceOption>()
            : KokoroCatalog.Voices.Select(v => new VoiceOption(KokoroCatalog.Prefix + v.Id, Loc.Format("Voice_NaturalVoice", v.Name))))
        .Concat(_speaker.Voices().Select(n => new VoiceOption(n, n))).ToList();

    /// <summary>The chosen voice's name (null = the default: Heart when a natural voice is downloaded, else the Windows default).</summary>
    public string? SelectedVoice
    {
        get => _settings.VoiceName;
        set { _settings.VoiceName = value; OnPropertyChanged(); }
    }
}
