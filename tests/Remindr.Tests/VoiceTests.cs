using System.IO;
using System.Net.Http;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;
using Remindr.Services.Assistant;
using Remindr.ViewModels;
using Remindr.Views.Dialogs;

namespace Remindr.Tests;

/// <summary>Phase 5 part 2, slices 7-9: the voice orb's rules, push-to-talk, the yes/no matcher, spoken text and the voice settings. No microphone, model or speaker is needed.</summary>
public class VoiceTests
{
    // ---- slice 7: orb state machine ----------------------------------------------------------------

    [Theory]
    [InlineData(true, false, false, false, OrbState.Idle)]
    [InlineData(false, false, false, false, OrbState.Muted)]
    [InlineData(false, false, false, true, OrbState.Thinking)]   // the text model running beats "voice off"
    [InlineData(true, true, true, true, OrbState.Listening)]
    [InlineData(true, false, true, true, OrbState.Speaking)]
    [InlineData(true, false, false, true, OrbState.Thinking)]
    public void Orb_StateWins_ByPriority(bool voiceOn, bool listening, bool speaking, bool thinking, OrbState expected) =>
        Assert.Equal(expected, OrbModel.Resolve(voiceOn, listening, speaking, thinking));

    [Fact]
    public void Orb_Level_IsSmoothed_ClampedAndOnlyFollowedWhileListeningOrSpeaking()
    {
        var up = OrbModel.Smooth(0, 1);
        Assert.InRange(up, 0.5, 0.7);                      // quick attack
        var down = OrbModel.Smooth(1, 0);
        Assert.InRange(down, 0.7, 0.9);                    // slower release
        Assert.Equal(1.0, OrbModel.Smooth(1, 5));
        Assert.Equal(0.0, OrbModel.Smooth(0, -3));

        Assert.Equal(0.6, OrbModel.EffectiveLevel(OrbState.Listening, 0.6));
        Assert.Equal(0.6, OrbModel.EffectiveLevel(OrbState.Speaking, 0.6));
        foreach (var s in new[] { OrbState.Idle, OrbState.Thinking, OrbState.Muted })
            Assert.Equal(0.0, OrbModel.EffectiveLevel(s, 0.9));
        Assert.Equal(1.0, OrbModel.EffectiveLevel(OrbState.Listening, 7));
    }

    [Fact]
    public void Orb_LevelFromRms_LiftsQuietSpeech_AndStaysInRange()
    {
        Assert.Equal(0, OrbModel.LevelFromRms(0));
        Assert.InRange(OrbModel.LevelFromRms(0.05), 0.35, 0.5);
        Assert.Equal(1, OrbModel.LevelFromRms(5));
    }

    [Fact]
    public void Orb_ReducedMotion_NeverMoves_ButStillNamesEveryState()
    {
        foreach (var s in Enum.GetValues<OrbState>())
        {
            Assert.False(OrbModel.Moves(s, effectsEnabled: false));
            Assert.NotEqual("", Loc.Get(OrbModel.NameKey(s)));
            Assert.NotEqual(OrbModel.NameKey(s), Loc.Get(OrbModel.NameKey(s)));   // the key exists in the resources
        }
        Assert.True(OrbModel.Moves(OrbState.Idle, true));
        Assert.False(OrbModel.Moves(OrbState.Muted, true));
    }

    [Fact]
    public void Orb_AnnouncesListeningThinkingSpeaking_Only()
    {
        Assert.True(OrbModel.Announces(OrbState.Listening));
        Assert.True(OrbModel.Announces(OrbState.Thinking));
        Assert.True(OrbModel.Announces(OrbState.Speaking));
        Assert.False(OrbModel.Announces(OrbState.Idle));
        Assert.False(OrbModel.Announces(OrbState.Muted));
        Assert.Equal("Listening", Loc.Get("Voice_Orb_Listening"));
    }

    // ---- slice 9: yes/no matcher and spoken text -----------------------------------------------------

    [Theory]
    [InlineData("yes", SpokenAnswer.Yes)]
    [InlineData("Yes.", SpokenAnswer.Yes)]
    [InlineData("  Confirm! ", SpokenAnswer.Yes)]
    [InlineData("do it", SpokenAnswer.Yes)]
    [InlineData("Do it.", SpokenAnswer.Yes)]
    [InlineData("no", SpokenAnswer.No)]
    [InlineData("No!", SpokenAnswer.No)]
    [InlineData("cancel", SpokenAnswer.No)]
    [InlineData("yes please", SpokenAnswer.None)]        // only the exact short answers
    [InlineData("no problem", SpokenAnswer.None)]
    [InlineData("yeah", SpokenAnswer.None)]
    [InlineData("remind me to say yes", SpokenAnswer.None)]
    [InlineData("", SpokenAnswer.None)]
    [InlineData(null, SpokenAnswer.None)]
    public void Answer_MatchesOnlyTheExactShortAnswers(string? said, SpokenAnswer expected) => Assert.Equal(expected, VoiceText.Answer(said));

    [Fact]
    public void Confirm_SentenceTable()
    {
        string One(ChangeKind k, string when = "", bool imp = false) => VoiceText.Confirm(new[] { new VoiceText.Line(k, "call mom", when, imp) });
        Assert.Equal("Add 'call mom' Friday at 6 PM?", One(ChangeKind.Create, "Friday at 6 PM"));
        Assert.Equal("Add 'call mom' Fri, Nov 6, 6:00 PM, marked important?", One(ChangeKind.Create, "Fri, Nov 6 · 6:00 PM", true));
        Assert.Equal("Change 'call mom' to Tuesday at 3 PM?", One(ChangeKind.Update, "Tuesday at 3 PM"));
        Assert.Equal("Mark 'call mom' done?", One(ChangeKind.Complete));
        Assert.Equal("Delete 'call mom'?", One(ChangeKind.Delete));
        Assert.Equal("Snooze 'call mom' for 1 hour?", One(ChangeKind.Snooze, "for 1 hour"));
        Assert.Equal("Make these 2 changes?", VoiceText.Confirm(new[] { new VoiceText.Line(ChangeKind.Create, "a", "", false), new VoiceText.Line(ChangeKind.Create, "b", "", false) }));
        Assert.Equal("", VoiceText.Confirm(Array.Empty<VoiceText.Line>()));
    }

    [Fact]
    public void Short_DropsEmptyAndLongReplies()
    {
        Assert.Equal("Done.", VoiceText.Short("  Done. "));
        Assert.Null(VoiceText.Short(""));
        Assert.Null(VoiceText.Short(null));
        Assert.Null(VoiceText.Short(new string('x', VoiceText.MaxSpokenLength + 1)));
    }

    // ---- slice 8: push-to-talk controller ------------------------------------------------------------

    private sealed class FakeRecorder : IRecorder
    {
        public int Starts, Stops, Aborts;
        public bool Open;
        public int SampleCount = 16000;
        public MicException? FailWith;
        public Action<double>? Rms;
        public void Start(Action<double> rms) { if (FailWith is not null) throw FailWith; Starts++; Open = true; Rms = rms; }
        public float[] Stop() { Stops++; Open = false; return Enumerable.Repeat(0.5f, SampleCount).ToArray(); }
        public void Abort() { Aborts++; Open = false; }
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public string Text = "remind me to call grandma";
        public float[]? Received;
        public TaskCompletionSource? Gate;
        public int Calls;
        public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct)
        {
            Calls++;
            Received = samples;
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
            return Text;
        }
    }

    private sealed class Rig
    {
        public readonly FakeRecorder Rec = new();
        public readonly FakeTranscriber Tr = new();
        public readonly PushToTalkController Ptt;
        public readonly List<string> Heard = new();
        public readonly List<VoiceError> Errors = new();
        public readonly List<double> Levels = new();

        public Rig()
        {
            Ptt = new PushToTalkController(Rec, Tr);
            Ptt.Transcribed += Heard.Add;
            Ptt.Failed += Errors.Add;
            Ptt.Level += Levels.Add;
        }
    }

    [Fact]
    public async Task AForgottenButtonRecording_ClosesTheMicByItself()
    {
        var r = new Rig();
        r.Ptt.MaxRecording = TimeSpan.FromMilliseconds(50);
        Assert.True(r.Ptt.Begin(PttSource.Button));
        Assert.True(SpinWait.SpinUntil(() => !r.Rec.Open, 2000));
        await r.Ptt.Pending!;
        Assert.Equal(new[] { "remind me to call grandma" }, r.Heard);

        // A timer from an earlier recording never cuts a newer one short.
        r.Ptt.MaxRecording = TimeSpan.FromSeconds(60);
        Assert.True(r.Ptt.Begin(PttSource.Button));
        await Task.Delay(150);
        Assert.True(r.Rec.Open);
        r.Ptt.Cancel();
    }

    [Fact]
    public async Task HoldAndRelease_RecordsThenTranscribes_AndTheMicIsOpenOnlyWhileHeld()
    {
        var r = new Rig();
        Assert.False(r.Rec.Open);
        Assert.True(r.Ptt.Begin(PttSource.Key));
        Assert.True(r.Rec.Open);
        Assert.Equal(PttState.Recording, r.Ptt.State);

        r.Ptt.End(PttSource.Key);
        Assert.False(r.Rec.Open);                       // closed on release, before the transcript is even back
        await r.Ptt.Pending!;
        Assert.Equal(new[] { "remind me to call grandma" }, r.Heard);
        Assert.Equal(PttState.Idle, r.Ptt.State);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void KeyRepeat_WhileHeld_DoesNotRestartTheRecording()
    {
        var r = new Rig();
        Assert.True(r.Ptt.Begin(PttSource.Key));
        for (var i = 0; i < 5; i++) Assert.False(r.Ptt.Begin(PttSource.Key));
        Assert.Equal(1, r.Rec.Starts);
    }

    [Fact]
    public async Task AKeyHold_EndsOnlyByTheKey_AndAButtonToggleOnlyByTheButton()
    {
        var r = new Rig();
        r.Ptt.Begin(PttSource.Key);
        r.Ptt.Toggle();                                  // the button does not end a key hold
        Assert.Equal(PttState.Recording, r.Ptt.State);
        r.Ptt.End(PttSource.Key);
        await r.Ptt.Pending!;

        r.Ptt.Toggle();                                  // idle: starts
        Assert.Equal(PttState.Recording, r.Ptt.State);
        r.Ptt.End(PttSource.Key);                        // releasing Ctrl+Space does nothing to a toggle
        Assert.Equal(PttState.Recording, r.Ptt.State);
        r.Ptt.Toggle();                                  // second click: transcribes
        await r.Ptt.Pending!;
        Assert.Equal(2, r.Heard.Count);
    }

    [Fact]
    public void Esc_OrLostFocus_DropsTheRecording_WithoutTranscribing()
    {
        var r = new Rig();
        r.Ptt.Begin(PttSource.Key);
        r.Ptt.Cancel();
        Assert.Equal(1, r.Rec.Aborts);
        Assert.Equal(0, r.Rec.Stops);
        Assert.False(r.Rec.Open);
        Assert.Equal(PttState.Idle, r.Ptt.State);
        r.Ptt.End(PttSource.Key);                        // the late key-up after losing focus is harmless
        Assert.Equal(0, r.Tr.Calls);
        Assert.Empty(r.Heard);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public async Task EscWhileTranscribing_DiscardsTheTranscript()
    {
        var r = new Rig();
        r.Tr.Gate = new TaskCompletionSource();
        r.Ptt.Begin(PttSource.Key);
        r.Ptt.End(PttSource.Key);
        Assert.Equal(PttState.Transcribing, r.Ptt.State);
        r.Ptt.Cancel();
        await r.Ptt.Pending!;
        Assert.Empty(r.Heard);
        Assert.Empty(r.Errors);
        Assert.Equal(PttState.Idle, r.Ptt.State);
    }

    [Fact]
    public async Task ATap_ShorterThanAThirdOfASecond_IsNothingHeard()
    {
        var r = new Rig();
        r.Rec.SampleCount = PushToTalkController.MinSamples - 1;
        r.Ptt.Begin(PttSource.Key);
        r.Ptt.End(PttSource.Key);
        Assert.Null(r.Ptt.Pending);
        await Task.CompletedTask;
        Assert.Equal(new[] { VoiceError.NothingHeard }, r.Errors);
        Assert.Equal(0, r.Tr.Calls);
    }

    [Theory]
    [InlineData(VoiceError.NoMicrophone)]
    [InlineData(VoiceError.AccessDenied)]
    public void NoMicOrDeniedPermission_IsReported_AndNothingStarts(VoiceError kind)
    {
        var r = new Rig();
        r.Rec.FailWith = new MicException(kind);
        Assert.False(r.Ptt.Begin(PttSource.Button));
        Assert.Equal(new[] { kind }, r.Errors);
        Assert.Equal(PttState.Idle, r.Ptt.State);
    }

    [Fact]
    public async Task AnEmptyTranscript_AndNonSpeechTags_AreNothingHeard()
    {
        var r = new Rig();
        r.Tr.Text = " [BLANK_AUDIO] ";
        r.Ptt.Begin(PttSource.Key);
        r.Ptt.End(PttSource.Key);
        await r.Ptt.Pending!;
        Assert.Empty(r.Heard);
        Assert.Equal(new[] { VoiceError.NothingHeard }, r.Errors);
        Assert.Equal("hello there", PushToTalkController.Clean(" [MUSIC]  hello (laughs)  there "));
    }

    [Fact]
    public async Task Audio_StaysInMemory_IsZeroedAfterTranscription_AndWritesNoFiles()
    {
        var tempBefore = Directory.GetFiles(Path.GetTempPath(), "*", SearchOption.TopDirectoryOnly).ToHashSet();
        var r = new Rig();
        r.Ptt.Begin(PttSource.Key);
        r.Rec.Rms!(0.1);
        r.Ptt.End(PttSource.Key);
        await r.Ptt.Pending!;

        Assert.NotNull(r.Tr.Received);
        Assert.All(r.Tr.Received!, s => Assert.Equal(0f, s));            // discarded after use
        Assert.NotEmpty(r.Levels);                                        // the level reached the listeners
        Assert.All(r.Levels, l => Assert.InRange(l, 0, 1));
        var created = Directory.GetFiles(Path.GetTempPath(), "*", SearchOption.TopDirectoryOnly).Where(f => !tempBefore.Contains(f)).ToList();
        Assert.DoesNotContain(created, f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase));
    }

    // ---- voice settings and model entry ----------------------------------------------------------------

    [Fact]
    public void VoiceSettings_AreOffByDefault_AndRoundTrip()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var s = new VoiceSettings(new SettingsRepo(t.Db));
        Assert.False(s.Enabled);                         // voice input is off by default
        Assert.True(s.SpeakReplies);
        Assert.Null(s.VoiceName);

        var changes = 0;
        s.Changed += (_, _) => changes++;
        s.Enabled = true;
        s.SpeakReplies = false;
        s.VoiceName = "Microsoft Zira";
        var again = new VoiceSettings(new SettingsRepo(t.Db));
        Assert.True(again.Enabled);
        Assert.False(again.SpeakReplies);
        Assert.Equal("Microsoft Zira", again.VoiceName);
        Assert.Equal(3, changes);
        s.VoiceName = null;
        Assert.Null(new VoiceSettings(new SettingsRepo(t.Db)).VoiceName);
    }

    [Fact]
    public void AudioModel_IsPinned_AndOnlyHuggingFaceAndThatLlamaCppReleaseAreAllowed()
    {
        Assert.Equal(3, AudioModelCatalog.Parts.Count);
        foreach (var (tier, url) in AudioModelCatalog.Parts)
        {
            Assert.Matches("^[0-9a-f]{64}$", tier.Sha256);
            Assert.True(tier.Bytes > 0);
            Assert.EndsWith("/" + tier.File, url.ToString());
            Assert.True(AudioModelCatalog.IsAllowedHost(url));
        }
        // The model files come from a pinned commit, so the pinned hashes can't drift.
        Assert.Contains("/resolve/39670cb5c42e9c4bfe046519ba7ffbe47f9552ad/", AudioModelCatalog.Parts[0].Url.ToString());
        Assert.Equal("LFM3-Audio-2B-Q4_K_M.gguf", AudioModelCatalog.Model.File);
        Assert.True(AudioModelCatalog.IsAllowedHost(new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/x")));
        Assert.False(AudioModelCatalog.IsAllowedHost(new Uri("https://github.com/someone/else/releases/download/b1/llama.zip")));
        Assert.False(AudioModelCatalog.IsAllowedHost(new Uri("http://github.com/ggml-org/llama.cpp/releases/download/b11960/x.zip")));
        Assert.False(AudioModelCatalog.IsAllowedHost(new Uri("https://evil.example/x.gguf")));
        Assert.StartsWith("Voice model: hears you and talks back", Loc.Get("Voice_Model_Name"));
    }

    [Fact]
    public void VoiceModelManager_IsInstalledOnlyWithEveryFile_ShowsALeftoverAsPaused_AndCancelDeletesItAll()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        var model = ModelCatalog.PathOf(AudioModelCatalog.Model, t.Dir);
        var part = ModelDownloader.PartPath(t.Dir, AudioModelCatalog.Projector);
        File.WriteAllBytes(model, new byte[1]);
        File.WriteAllBytes(part, new byte[1234]);
        var m = new VoiceModelManager(t.Dir, new ModelDownloader(new HttpClientHandler()));
        Assert.False(m.Installed);
        Assert.Equal(ModelDownloadState.Paused, m.State);
        Assert.Equal(AudioModelCatalog.Model.Bytes + 1234, m.BytesDone);
        Assert.Equal(AudioModelCatalog.Parts.Sum(p => p.Tier.Bytes), m.TotalBytes);
        m.Cancel();
        Assert.False(File.Exists(part));
        Assert.False(File.Exists(model));   // half a voice is useless: cancel frees it all
        Assert.Equal(ModelDownloadState.Idle, m.State);
    }

    [Fact]
    public void VoiceModelManager_Remove_LetsTheServerGoBeforeDeleting()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        foreach (var (tier, _) in AudioModelCatalog.Parts) File.WriteAllBytes(ModelCatalog.PathOf(tier, t.Dir), new byte[1]);
        Directory.CreateDirectory(AudioModelCatalog.RuntimeDir(t.Dir));
        var m = new VoiceModelManager(t.Dir, new ModelDownloader(new HttpClientHandler()));
        Assert.True(m.Installed);
        bool? presentWhileRemoving = null;
        m.Removing += (_, _) => presentWhileRemoving = File.Exists(ModelCatalog.PathOf(AudioModelCatalog.Model, t.Dir));
        m.Remove();
        Assert.True(presentWhileRemoving);
        Assert.False(m.Installed);
        Assert.False(Directory.Exists(AudioModelCatalog.RuntimeDir(t.Dir)));
    }

    [Fact]
    public void LfmSpeaker_PicksTheNaturalVoiceForTheDefaultAndItsOwnVoices_AndWindowsForTheRest()
    {
        Assert.Equal("female", LfmSpeaker.NaturalId(null));
        Assert.Equal("male", LfmSpeaker.NaturalId(AudioModelCatalog.Prefix + "male"));
        Assert.Equal("female", LfmSpeaker.NaturalId("kokoro:bf_emma"));   // a 0.5.0 Kokoro choice becomes the default natural voice
        Assert.Null(LfmSpeaker.NaturalId("Microsoft Zira"));
        Assert.Equal(new[] { "Done.", "You have three things tomorrow!", "Want more?" }, LfmSpeaker.Sentences(" Done. You have three things tomorrow!  Want more? "));
    }

    [Fact]
    public async Task LfmSpeaker_WithNoModel_UsesWindows_AndDropsAStaleNaturalChoice()
    {
        using var t = new TempDb();
        var windows = new RecordingSpeaker();
        using var server = new LfmAudioServer(t.Dir, () => GpuMode.Off);
        var speaker = new LfmSpeaker(server, () => false, windows);
        await speaker.SpeakAsync("hi", "Microsoft Zira", null, default);
        await speaker.SpeakAsync("hi", AudioModelCatalog.Prefix + "male", null, default);
        Assert.Equal(new string?[] { "Microsoft Zira", null }, windows.Voices_);
        Assert.Null(server.Device);   // nothing was started
    }

    [Fact]
    public async Task LfmAudioServer_WithoutTheRuntime_SaysNotInstalled()
    {
        using var t = new TempDb();
        Directory.CreateDirectory(t.Dir);
        using var server = new LfmAudioServer(t.Dir, () => GpuMode.Auto);
        await Assert.ThrowsAsync<FileNotFoundException>(() => server.ClientAsync(default));
    }

    [Fact]
    public void Wav_IsA16BitMonoPcmHeader_WithClampedSamples()
    {
        var wav = Wav.FromSamples(new[] { 0f, 1f, -2f }, 16000);
        Assert.Equal(44 + 6, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(6, BitConverter.ToInt32(wav, 40));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 46));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(wav, 48));
    }

    private sealed class RecordingSpeaker : ISpeaker
    {
        public List<string?> Voices_ { get; } = new();
        public Task SpeakAsync(string text, string? voiceName, Action<double>? level, CancellationToken ct) { Voices_.Add(voiceName); return Task.CompletedTask; }
        public IReadOnlyList<string> Voices() => Array.Empty<string>();
    }

    // ---- the assistant page's voice flow ------------------------------------------------------------------

    private sealed class ScriptedModel : IAssistantModel
    {
        public string? Device => null;
        public string? NoteKey => null;
        public Task<string> InferAsync(string phrase, CancellationToken ct = default) =>
            Task.FromResult("""[{"name":"create_reminders","arguments":{"items":[{"title":"call mom","time":"6"}]}}]""");
        public async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public void Dispose() { }
    }

    private sealed class FakeSpeaker : ISpeaker
    {
        public readonly List<string> Said = new();
        public IReadOnlyList<string> Voices() => new[] { "Test Voice" };
        public Task SpeakAsync(string text, string? voiceName, Action<double>? level, CancellationToken ct) { Said.Add(text); return Task.CompletedTask; }
    }

    private sealed class VmRig : IDisposable
    {
        public readonly ReminderEnv E = new(new DateTime(2026, 11, 5, 14, 0, 0));
        public readonly Rig Voice = new();
        public readonly FakeSpeaker Speaker = new();
        public readonly VoiceSettings Settings;
        public readonly AssistantViewModel Vm;

        public VmRig(bool voiceOn)
        {
            var settings = new SettingsRepo(E.T.Db);
            Settings = new VoiceSettings(settings) { Enabled = voiceOn };
            Directory.CreateDirectory(E.T.Dir);
            var file = Path.Combine(E.T.Dir, "m.gguf");
            File.WriteAllText(file, "x");
            var service = new AssistantService(settings, _ => new ScriptedModel(), file) { Enabled = true };
            var models = new ModelManager(Path.Combine(E.T.Dir, "models"), settings, new ModelDownloader(new HttpClientHandler()), () => 64L << 30);
            var voiceDir = Path.Combine(E.T.Dir, "voice");
            Directory.CreateDirectory(voiceDir);
            foreach (var (tier, _) in AudioModelCatalog.Parts) File.WriteAllBytes(ModelCatalog.PathOf(tier, voiceDir), new byte[1]);
            Vm = new AssistantViewModel(service, E.Repo, new CategoryRepo(E.T.Db), E.Session,
                new ReminderDialogs(null!, E.Repo, new CategoryRepo(E.T.Db), E.Session, E.Clock), E.Clock, settings, new ModelPickerViewModel(models),
                Voice.Ptt, Speaker, Settings, new VoiceModelManager(voiceDir, new ModelDownloader(new HttpClientHandler())));
            Vm.IsShown = true;
        }

        public async Task Say(string text)
        {
            Vm.Input = text;
            await Vm.SendCommand.ExecuteAsync(null);
        }

        /// <summary>Hold and release the hotkey with the fake transcriber answering <paramref name="transcript"/>.</summary>
        public async Task Speak(string transcript)
        {
            Voice.Tr.Text = transcript;
            Vm.BeginHold();
            Vm.EndHold();
            await Voice.Ptt.Pending!;
            await Task.Delay(50);   // the transcript is handed to the view model after the task completes
        }

        public void Dispose() => E.Dispose();
    }

    [Fact]
    public void Orb_IsMuted_WhileVoiceIsOff_AndIdleOnceItIsOn()
    {
        using var off = new VmRig(voiceOn: false);
        Assert.Equal(OrbState.Muted, off.Vm.Orb);
        Assert.False(off.Vm.CanHold);
        off.Vm.ToggleMicCommand.Execute(null);
        Assert.Equal(Loc.Get("Voice_Status_Off"), off.Vm.VoiceStatus);
        Assert.Equal(0, off.Voice.Rec.Starts);            // the mic is never opened while voice is off

        using var on = new VmRig(voiceOn: true);
        Assert.Equal(OrbState.Idle, on.Vm.Orb);
        Assert.True(on.Vm.CanHold);
    }

    [Fact]
    public async Task Hotkey_Transcript_RunsThroughWork_ThenSpokenYesConfirmsTheCard()
    {
        using var r = new VmRig(voiceOn: true);
        await r.Speak("remind me to call mom at 6");
        var card = Assert.IsType<ConfirmBubble>(r.Vm.Messages.Last());
        Assert.True(card.IsPending);
        Assert.Empty(r.E.Repo.ListAll());                  // nothing written before a confirm
        Assert.Contains(r.Speaker.Said, s => s.StartsWith("Add 'call mom'"));   // the card's question was read out

        await r.Speak("Yes.");
        Assert.False(card.IsPending);
        Assert.Single(r.E.Repo.ListAll());
        Assert.Equal(OrbState.Idle, r.Vm.Orb);
    }

    [Fact]
    public async Task SpokenNo_CancelsTheCard_AndOtherWordsAreSentAsText()
    {
        using var r = new VmRig(voiceOn: true);
        await r.Speak("remind me to call mom at 6");
        var card = (ConfirmBubble)r.Vm.Messages.Last();
        await r.Speak("cancel");
        Assert.False(card.IsPending);
        Assert.Empty(r.E.Repo.ListAll());

        await r.Speak("remind me to call mom at 6");
        var second = (ConfirmBubble)r.Vm.Messages.Last();
        await r.Speak("yes please");                       // not an exact answer: becomes a new request, the first card stays
        Assert.True(second.IsPending);
        Assert.Equal(3, r.Vm.Messages.OfType<ConfirmBubble>().Count());
    }

    [Fact]
    public async Task Esc_StopsVoice_AndNoMicIsSurfacedAsAMessage()
    {
        using var r = new VmRig(voiceOn: true);
        r.Voice.Rec.FailWith = new MicException(VoiceError.AccessDenied);
        r.Vm.BeginHold();
        await Task.Delay(20);
        Assert.Equal(Loc.Get("Voice_Err_Denied"), r.Vm.VoiceStatus);
        Assert.True(r.Vm.ShowMicLink);

        r.Voice.Rec.FailWith = new MicException(VoiceError.NoMicrophone);
        r.Vm.BeginHold();
        Assert.Equal(Loc.Get("Voice_Err_NoMic"), r.Vm.VoiceStatus);
        Assert.False(r.Vm.ShowMicLink);

        r.Voice.Rec.FailWith = null;
        r.Vm.BeginHold();
        Assert.Equal(OrbState.Listening, r.Vm.Orb);
        r.Vm.StopVoice();                                  // Esc
        Assert.Equal(OrbState.Idle, r.Vm.Orb);
        Assert.False(r.Voice.Rec.Open);
    }

    [Fact]
    public async Task SpeakReplies_Off_StaysSilent()
    {
        using var r = new VmRig(voiceOn: true);
        r.Settings.SpeakReplies = false;
        await r.Say("remind me to call mom at 6");
        Assert.Empty(r.Speaker.Said);
    }
}
