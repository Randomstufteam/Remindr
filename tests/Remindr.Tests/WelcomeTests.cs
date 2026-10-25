using Remindr.Services;

namespace Remindr.Tests;

public class GreetingCatalogTests
{
    [Fact]
    public void All_HasThePlannedWordsInOrder()
    {
        var words = GreetingCatalog.All.Select(g => g.Word).ToArray();
        Assert.Equal(new[]
        {
            "Hello", "Hola", "Bonjour", "Hallo", "Ciao", "Olá", "こんにちは", "안녕하세요", "你好", "مرحبا", "שלום", "नमस्ते",
            "Привет", "Merhaba", "Γεια σας", "Hej", "Cześć", "Xin chào", "สวัสดี", "Halo", "Kumusta"
        }, words);
    }

    [Fact]
    public void EveryGreeting_IsTaggedWithASupportedLanguage_AndOnlyArabicAndHebrewAreRtl()
    {
        foreach (var g in GreetingCatalog.All)
        {
            var info = LocalizationService.Find(g.Code);
            Assert.NotNull(info);
            Assert.Equal(info!.IsRtl, g.IsRtl);
            Assert.False(string.IsNullOrWhiteSpace(g.Word));
        }
        Assert.Equal(new[] { "ar", "he" }, GreetingCatalog.All.Where(g => g.IsRtl).Select(g => g.Code).ToArray());
    }

    [Theory]
    [InlineData("en", "Hello")]
    [InlineData("de", "Hallo")]
    [InlineData("de-AT", "Hallo")]
    [InlineData("ja", "こんにちは")]
    [InlineData("ar", "مرحبا")]
    [InlineData("zh-Hans", "你好")]
    [InlineData("pt-PT", "Olá")]
    [InlineData("pt-BR", "Olá")]
    [InlineData("fil", "Kumusta")]
    [InlineData("nl", "Hello")]          // no word for Dutch: starts with English
    [InlineData("xx-XX", "Hello")]
    [InlineData(null, "Hello")]
    public void OrderedFor_StartsWithTheWordForTheUiLanguage(string? ui, string first)
    {
        Assert.Equal(first, GreetingCatalog.OrderedFor(ui)[0].Word);
    }

    [Fact]
    public void OrderedFor_KeepsEveryWordExactlyOnce()
    {
        var ordered = GreetingCatalog.OrderedFor("ko");
        Assert.Equal(GreetingCatalog.All.Count, ordered.Count);
        Assert.Equal(GreetingCatalog.All.Select(g => g.Word).OrderBy(w => w), ordered.Select(g => g.Word).OrderBy(w => w));
        // Rotated, not shuffled: the word after the first is the one that follows it in the fixed order.
        Assert.Equal("Привет", GreetingCatalog.OrderedFor("hi")[1].Word);
    }

    [Fact]
    public void BlobColors_GiveFourColoursPerWord_AndEveryWordGetsADistinctHue()
    {
        var firsts = new HashSet<(byte, byte, byte)>();
        for (var i = 0; i < GreetingCatalog.All.Count; i++)
        {
            var colors = GreetingCatalog.BlobColors(i);
            Assert.Equal(4, colors.Count);
            Assert.Equal(4, colors.Distinct().Count());
            firsts.Add(colors[0]);
        }
        Assert.Equal(GreetingCatalog.All.Count, firsts.Count);
    }

    [Fact]
    public void NeighbouringWords_ShiftTheHueClearly()
    {
        for (var i = 0; i < GreetingCatalog.All.Count - 1; i++)
        {
            var diff = Math.Abs(GreetingCatalog.HueFor(i + 1) - GreetingCatalog.HueFor(i));
            diff = Math.Min(diff, 360 - diff);
            Assert.True(diff >= 30, $"words {i} and {i + 1} differ by only {diff} degrees");
        }
    }

    [Fact]
    public void FromHsl_MatchesKnownColours()
    {
        Assert.Equal(((byte)255, (byte)0, (byte)0), GreetingCatalog.FromHsl(0, 1, 0.5));
        Assert.Equal(((byte)0, (byte)255, (byte)0), GreetingCatalog.FromHsl(120, 1, 0.5));
        Assert.Equal(((byte)0, (byte)0, (byte)255), GreetingCatalog.FromHsl(240, 1, 0.5));
        Assert.Equal(((byte)255, (byte)255, (byte)255), GreetingCatalog.FromHsl(0, 0, 1));
    }

    [Fact]
    public void WordPeriod_HoldsAboutOnePointThreeSecondsPlusTheOverlappingTransition()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2250), GreetingCatalog.WordPeriod);
    }
}

public class WelcomeControllerTests
{
    [Fact]
    public void Replay_RaisesTheEvent_AndChangesNothingElse()
    {
        var c = new WelcomeController();
        var raised = 0;
        c.ReplayRequested += (_, _) => raised++;
        c.Replay();
        c.Replay();
        Assert.Equal(2, raised);
        Assert.False(c.ShowOnStart);
    }

    [Theory]
    [InlineData("--welcome", true)]
    [InlineData("--WELCOME", true)]
    [InlineData("--minimized", false)]
    public void HasWelcomeArg_FindsTheDebugSwitch(string arg, bool expected)
    {
        Assert.Equal(expected, WelcomeController.HasWelcomeArg(new[] { "x", arg }));
    }

    [Fact]
    public void HasWelcomeArg_HandlesNoArguments()
    {
        Assert.False(WelcomeController.HasWelcomeArg(null));
        Assert.False(WelcomeController.HasWelcomeArg(Array.Empty<string>()));
    }
}
