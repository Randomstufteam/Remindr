using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Remindr.Data;
using Remindr.Data.Repositories;
using Remindr.Services;
using Remindr.Services.Assistant;
using Remindr.ViewModels;
using Remindr.Views.Dialogs;

namespace Remindr.Tests;

/// <summary>Phase 5 part 2, slice 4: the model catalog and the downloader (a fake HTTP handler, a tiny "model", no network).</summary>
public class ModelCatalogTests
{
    private const long GB = 1L << 30;

    [Fact]
    public void EveryTier_HasAFileASizeAndA64HexHash()
    {
        Assert.Equal(new[] { "q4", "q8", "bf16" }, ModelCatalog.Tiers.Select(t => t.Id));
        foreach (var t in ModelCatalog.Tiers)
        {
            Assert.EndsWith(".gguf", t.File);
            Assert.True(t.Bytes > 500_000_000);
            Assert.Matches("^[0-9a-f]{64}$", t.Sha256);
        }
        Assert.Equal("q4", ModelCatalog.Default.Id);
        Assert.Equal(730_895_168, ModelCatalog.Tiers[0].Bytes);
        Assert.Equal(1_246_253_888, ModelCatalog.Tiers[1].Bytes);
        Assert.Equal(2_343_326_528, ModelCatalog.Tiers[2].Bytes);
    }

    [Theory]
    [InlineData(3, "")]
    [InlineData(4, "q4")]
    [InlineData(5, "q4")]
    [InlineData(6, "q4,q8")]
    [InlineData(7, "q4,q8")]
    [InlineData(8, "q4,q8,bf16")]
    [InlineData(32, "q4,q8,bf16")]
    public void CanRun_GreysOutTiersByTotalRam(long gb, string expected) =>
        Assert.Equal(expected, string.Join(",", ModelCatalog.Tiers.Where(t => ModelCatalog.CanRun(t, gb * GB)).Select(t => t.Id)));

    [Fact]
    public void ThisPcReportsSomeRam() => Assert.True(ModelCatalog.TotalRam() > GB);

    [Theory]
    [InlineData("https://huggingface.co/x", true)]
    [InlineData("https://cdn-lfs.hf.co/x", true)]
    [InlineData("https://us.aws.cdn.hf.co/xet-bridge-us/abc", true)]
    [InlineData("https://evilhuggingface.co/x", false)]
    [InlineData("https://huggingface.co.evil.com/x", false)]
    [InlineData("http://huggingface.co/x", false)]
    public void OnlyHuggingFaceHostsOverHttpsAreAllowed(string url, bool ok) => Assert.Equal(ok, ModelCatalog.IsAllowedHost(new Uri(url)));
}

public class ModelDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "remindr-models-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Data = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7 % 251)).ToArray();
    private static readonly ModelTier Tier = new("t", "t.gguf", Data.Length, Convert.ToHexString(SHA256.HashData(Data)).ToLowerInvariant(), 0);
    private static readonly Uri Url = new("https://huggingface.co/x/resolve/main/t.gguf");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private ModelDownloader Make(Handler h) => new(h, hostOk: null, freeSpace: _ => long.MaxValue);

    private static HttpResponseMessage Full(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Partial(byte[] all, int from)
    {
        var r = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(all[from..]) };
        r.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, all.Length - 1, all.Length);
        return r;
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(40, 100, 40)]
    [InlineData(99, 100, 99)]
    [InlineData(100, 100, 0)]
    [InlineData(150, 100, 0)]
    public void ResumeOffset_ContinuesOnlyAPartialFileSmallerThanTheModel(long part, long total, long expected) =>
        Assert.Equal(expected, ModelDownloader.ResumeOffset(part, total));

    [Fact]
    public async Task Download_WritesPart_VerifiesTheHash_ThenRenames()
    {
        var h = new Handler { Respond = _ => Full(Data) };
        var result = await Make(h).DownloadAsync(Tier, Url, _dir, null, null, default);
        Assert.Equal(ModelDownloadResult.Completed, result);
        Assert.Equal(Data, File.ReadAllBytes(Path.Combine(_dir, "t.gguf")));
        Assert.False(File.Exists(Path.Combine(_dir, "t.gguf.part")));
        Assert.Null(Assert.Single(h.Requests).Headers.Range);
    }

    [Fact]
    public async Task Resume_AsksForTheRestWithRange_AndAppends()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "t.gguf.part"), Data[..400]);
        var h = new Handler { Respond = r => Partial(Data, (int)r.Headers.Range!.Ranges.Single().From!.Value) };
        var result = await Make(h).DownloadAsync(Tier, Url, _dir, null, null, default);
        Assert.Equal(ModelDownloadResult.Completed, result);
        Assert.Equal(400, Assert.Single(h.Requests).Headers.Range!.Ranges.Single().From);
        Assert.Equal(Data, File.ReadAllBytes(Path.Combine(_dir, "t.gguf")));
    }

    [Fact]
    public async Task A200AnswerToARangeRequest_StartsOverInsteadOfAppending()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "t.gguf.part"), Data[..400]);
        var h = new Handler { Respond = _ => Full(Data) };
        var result = await Make(h).DownloadAsync(Tier, Url, _dir, null, null, default);
        Assert.Equal(ModelDownloadResult.Completed, result);
        Assert.Equal(Data, File.ReadAllBytes(Path.Combine(_dir, "t.gguf")));   // not 400 bytes too long
    }

    [Fact]
    public async Task AWrongHash_DeletesTheFile_AndNothingIsInstalled()
    {
        var bad = (byte[])Data.Clone();
        bad[10] ^= 0xFF;
        var h = new Handler { Respond = _ => Full(bad) };
        var result = await Make(h).DownloadAsync(Tier, Url, _dir, null, () => { }, default);
        Assert.Equal(ModelDownloadResult.HashMismatch, result);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task ARedirectToAnotherHost_IsRefused()
    {
        var h = new Handler
        {
            Respond = _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri("https://evil.example.com/t.gguf");
                return r;
            }
        };
        await Assert.ThrowsAsync<UpdateSecurityException>(() => Make(h).DownloadAsync(Tier, Url, _dir, null, null, default));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task ARedirectToTheHuggingFaceCdn_IsFollowed()
    {
        var h = new Handler
        {
            Respond = r =>
            {
                if (r.RequestUri!.Host == "huggingface.co")
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://us.aws.cdn.hf.co/xet/t.gguf");
                    return redirect;
                }
                return Full(Data);
            }
        };
        Assert.Equal(ModelDownloadResult.Completed, await Make(h).DownloadAsync(Tier, Url, _dir, null, null, default));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task NotEnoughDiskSpace_IsReportedBeforeAnythingIsDownloaded()
    {
        var h = new Handler { Respond = _ => Full(Data) };
        var dl = new ModelDownloader(h, null, _ => 10);
        await Assert.ThrowsAsync<NotEnoughSpaceException>(() => dl.DownloadAsync(Tier, Url, _dir, null, null, default));
        Assert.Empty(h.Requests);
    }

    /// <summary>A response body that sends a few bytes and then waits until cancelled (a download in progress).</summary>
    private sealed class StallingStream : Stream
    {
        private bool _sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_sent) { _sent = true; Data.AsMemory(0, 300).CopyTo(buffer); return 300; }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Pause_KeepsThePartFile_AndCancel_DeletesIt()
    {
        using var t = new TempDb();
        new DbInitializer(t.Db).Initialize();
        var h = new Handler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) } };
        var manager = new ModelManager(_dir, new SettingsRepo(t.Db), new ModelDownloader(h, null, _ => long.MaxValue), () => 64L << 30);
        var part = Path.Combine(_dir, "t.gguf.part");

        var running = manager.StartAsync(Tier);
        await WaitFor(() => manager.BytesDone == 300);
        manager.Pause();
        await running;
        Assert.Equal(ModelDownloadState.Paused, manager.State);
        Assert.Equal(300, new FileInfo(part).Length);

        manager.Cancel();   // from the paused state
        Assert.Equal(ModelDownloadState.Idle, manager.State);
        Assert.False(File.Exists(part));

        running = manager.StartAsync(Tier);
        await WaitFor(() => manager.BytesDone == 300);
        manager.Cancel();   // while running
        await running;
        Assert.Equal(ModelDownloadState.Idle, manager.State);
        Assert.False(File.Exists(part));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition(), "timed out");
    }
}

/// <summary>Slice 5: the "Use graphics card" rules.</summary>
public class GpuPolicyTests
{
    private const string Card = "NVIDIA GeForce RTX 4060 Laptop GPU";

    // mode, a dedicated card listed?, fully on it?  ->  device, note
    [Theory]
    [InlineData(GpuMode.Auto, true, true, Card, null)]
    [InlineData(GpuMode.Auto, true, false, null, null)]                 // partly offloaded: back to the CPU, quietly
    [InlineData(GpuMode.Auto, false, false, null, null)]                // only an integrated GPU: CPU
    [InlineData(GpuMode.On, true, true, Card, null)]
    [InlineData(GpuMode.On, true, false, null, GpuPolicy.NoCardNote)]
    [InlineData(GpuMode.On, false, false, null, GpuPolicy.NoCardNote)]
    [InlineData(GpuMode.Off, true, true, null, null)]
    [InlineData(GpuMode.Off, false, false, null, null)]
    public void Outcome_ForSettingCardAndOffload(GpuMode mode, bool hasCard, bool fullyOn, string? device, string? note) =>
        Assert.Equal((device, note), GpuPolicy.Outcome(mode, hasCard ? Card : null, fullyOn));

    [Fact]
    public void TheGpuIsOnlyTried_ForAutoOrOn_WithADedicatedCard()
    {
        Assert.True(GpuPolicy.TryGpu(GpuMode.Auto, Card));
        Assert.True(GpuPolicy.TryGpu(GpuMode.On, Card));
        Assert.False(GpuPolicy.TryGpu(GpuMode.Off, Card));
        Assert.False(GpuPolicy.TryGpu(GpuMode.Auto, null));
    }

    [Theory]
    [InlineData(null, GpuMode.Auto)]
    [InlineData("auto", GpuMode.Auto)]
    [InlineData("on", GpuMode.On)]
    [InlineData("off", GpuMode.Off)]
    [InlineData("junk", GpuMode.Auto)]
    public void Parse_DefaultsToAuto(string? value, GpuMode expected)
    {
        Assert.Equal(expected, GpuPolicy.Parse(value));
        Assert.Equal(expected, GpuPolicy.Parse(GpuPolicy.Format(expected)));
    }

    [Fact]
    public void ARealCapturedLog_ParsesTheCardAndTheOffload()
    {
        var log = new GpuLog();
        foreach (var line in new[]
        {
            "ggml_vulkan: 0 = NVIDIA GeForce RTX 4060 Laptop GPU (NVIDIA) | uma: 0 | fp16: 1 | bf16: 1 | warp size: 32 | shared memory: 49152 | int dot: 1 | matrix cores: NV_coopmat2",
            "ggml_vulkan: 1 = AMD Radeon 780M Graphics (AMD proprietary driver) | uma: 1 | fp16: 1 | bf16: 1 | warp size: 64 | shared memory: 32768 | int dot: 1 | matrix cores: KHR_coopmat",
            "llama_model_load_from_file_impl: using device Vulkan0 (NVIDIA GeForce RTX 4060 Laptop GPU) (0000:01:00.0) - 7188 MiB free",
            "load_tensors: offloaded 17/17 layers to GPU",
        }) log.Feed(line);
        Assert.Equal(Card, log.DedicatedCard);
        Assert.True(log.FullyOn(Card));
        Assert.Equal(4, log.Lines.Count);
    }
}

/// <summary>Slice 6: Work / Chat routing. A scripted model stands in for LLamaSharp.</summary>
public class AssistantModeTests
{
    private sealed class ScriptedModel : IAssistantModel
    {
        public int Infers, Chats;
        public string Call = """[{"name":"create_reminders","arguments":{"items":[{"title":"call mom","time":"6"}]}}]""";
        public string? Device => null;
        public string? NoteKey => null;
        public Task<string> InferAsync(string phrase, CancellationToken ct = default) { Infers++; return Task.FromResult(Call); }
        public async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Chats++;
            await Task.CompletedTask;
            yield return "Why did the ";
            yield return "chicken cross?";
        }
        public void Dispose() { }
    }

    private sealed class Rig : IDisposable
    {
        public readonly ReminderEnv E = new(new DateTime(2026, 11, 5, 14, 0, 0));
        public readonly ScriptedModel Model = new();
        public readonly AssistantViewModel Vm;
        public readonly SettingsRepo Settings;

        public Rig()
        {
            Settings = new SettingsRepo(E.T.Db);
            Directory.CreateDirectory(E.T.Dir);
            var file = Path.Combine(E.T.Dir, "m.gguf");
            File.WriteAllText(file, "x");
            var service = new AssistantService(Settings, _ => Model, file) { Enabled = true };
            var models = new ModelManager(Path.Combine(E.T.Dir, "models"), Settings, new ModelDownloader(new HttpClientHandler()), () => 64L << 30);
            Vm = new AssistantViewModel(service, E.Repo, new CategoryRepo(E.T.Db), E.Session,
                new ReminderDialogs(null!, E.Repo, new CategoryRepo(E.T.Db), E.Session, E.Clock), E.Clock, Settings, new ModelPickerViewModel(models));
        }

        public async Task Say(string text)
        {
            Vm.Input = text;
            await Vm.SendCommand.ExecuteAsync(null);
        }

        public void Dispose() => E.Dispose();
    }

    [Theory]
    [InlineData("remind me to call mom at 6", true)]
    [InlineData("delete milk", true)]
    [InlineData("what's on friday?", true)]
    [InlineData("Snooze the dentist for an hour", true)]
    [InlineData("I watered the cactus", true)]
    [InlineData("do I have any reminders today", true)]
    [InlineData("tell me a joke", false)]
    [InlineData("how are you today?", false)]
    [InlineData("what is the capital of France", false)]
    public void LooksLikeAction_IsAnInexpensiveHint(string phrase, bool expected) => Assert.Equal(expected, AssistantModes.LooksLikeAction(phrase));

    [Fact]
    public void ToolOf_ReadsTheCallName()
    {
        Assert.Equal("none", AssistantModes.ToolOf("""[{"name":"none","arguments":{}}]"""));
        Assert.True(AssistantModes.IsAction("""[{"name":"delete_reminder","arguments":{}}]"""));
        Assert.False(AssistantModes.IsAction("""[{"name":"none","arguments":{}}]"""));
        Assert.False(AssistantModes.IsAction("not json"));
        Assert.False(AssistantModes.IsAction(null));
    }

    [Fact]
    public void Recent_KeepsTheLastTwelveMessages()
    {
        var all = Enumerable.Range(0, 20).Select(i => new ChatTurn(i % 2 == 0, "m" + i)).ToList();
        var recent = AssistantModes.Recent(all);
        Assert.Equal(12, recent.Count);
        Assert.Equal("m8", recent[0].Text);
        Assert.Equal("m19", recent[^1].Text);
    }

    [Fact]
    public async Task Work_ShowsTheConfirmCard_AndChitChatShowsTheChatNote()
    {
        using var r = new Rig();
        Assert.True(r.Vm.IsWork);   // the default
        await r.Say("remind me to call mom at 6");
        Assert.IsType<ConfirmBubble>(r.Vm.Messages.Last());
        Assert.Empty(r.E.Repo.ListAll());

        r.Model.Call = """[{"name":"none","arguments":{}}]""";
        await r.Say("tell me a joke");
        Assert.Equal(Loc.Get("Assistant_SwitchToChat"), Assert.IsType<ReplyBubble>(r.Vm.Messages.Last()).Text);
        Assert.Equal(0, r.Model.Chats);
    }

    [Fact]
    public async Task Chat_RepliesToChitChat_WithoutTheToolPipeline()
    {
        using var r = new Rig();
        r.Vm.IsChat = true;
        await r.Say("tell me a joke");
        Assert.Equal("Why did the chicken cross?", Assert.IsType<StreamBubble>(r.Vm.Messages.Last()).Text);
        Assert.Equal(0, r.Model.Infers);
        Assert.Equal(1, r.Model.Chats);
    }

    [Fact]
    public async Task Chat_GivesADoThisInWorkChip_ForARequest_AndNeverWritesUntilConfirmed()
    {
        using var r = new Rig();
        r.Vm.IsChat = true;
        await r.Say("remind me to call mom at 6");
        var chip = Assert.IsType<WorkChip>(r.Vm.Messages.Last());
        Assert.Equal(0, r.Model.Chats);
        Assert.Empty(r.E.Repo.ListAll());

        chip.RunCommand.Execute(null);
        Assert.True(r.Vm.IsWork);
        var card = Assert.IsType<ConfirmBubble>(r.Vm.Messages.Last());
        Assert.Empty(r.E.Repo.ListAll());          // the card still has to be confirmed
        card.ConfirmCommand.Execute(null);
        Assert.Single(r.E.Repo.ListAll());
    }

    [Fact]
    public async Task Chat_ARequestLookingMessageThatIsNotOne_IsStillAnswered()
    {
        using var r = new Rig();
        r.Vm.IsChat = true;
        r.Model.Call = """[{"name":"none","arguments":{}}]""";
        await r.Say("what is the capital of France");
        Assert.IsType<StreamBubble>(r.Vm.Messages.Last());
    }

    [Fact]
    public void TheMode_IsRememberedPerProfile()
    {
        using var r = new Rig();
        Assert.Equal(AssistantMode.Work, AssistantModes.Read(r.Settings, r.E.A.Id));
        r.Vm.IsChat = true;                       // profile A
        Assert.Equal(AssistantMode.Chat, AssistantModes.Read(r.Settings, r.E.A.Id));
        Assert.Equal(AssistantMode.Work, AssistantModes.Read(r.Settings, r.E.B.Id));

        r.E.Session.SwitchTo(r.E.B);
        Assert.True(r.Vm.IsWork);
        r.E.Session.SwitchTo(r.E.A);
        Assert.True(r.Vm.IsChat);                 // and it survives "a restart": it is read from the database
    }
}
