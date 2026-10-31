using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Remindr.Services;

namespace Remindr.Tests;

/// <summary>
/// Phase 4 slice 22: tall scripts (Tibetan, Myanmar, Sinhala, Odia, Khmer) are taller than Latin, so anything that holds a label
/// must be able to grow. Pins the two places that used a fixed Height around text.
/// </summary>
public class ScriptClippingTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SourceTree.AppDir() }.Concat(parts).ToArray()));

    [Fact]
    public void NavItemStyle_GrowsWithItsLabel_InsteadOfAFixedHeight()
    {
        var xaml = Read("MainWindow.xaml");
        var start = xaml.IndexOf("x:Key=\"RemindrNavItemStyle\"", StringComparison.Ordinal);
        Assert.True(start > 0);
        var style = xaml[start..xaml.IndexOf("</Style>", start, StringComparison.Ordinal)];
        Assert.Matches(new Regex("Property=\"MinHeight\" Value=\"36\""), style);
        Assert.DoesNotMatch(new Regex("Property=\"Height\""), style);
    }

    [Fact]
    public void GreetingWordHost_GrowsWithTheWord_InsteadOfAFixedHeight()
    {
        var xaml = Read("Views", "Welcome", "WelcomeView.xaml");
        var tag = Regex.Match(xaml, "<Grid x:Name=\"WordHost\"[^>]*>").Value;
        Assert.Contains("MinHeight=\"190\"", tag);
        Assert.DoesNotMatch(new Regex(@"\sHeight="""), tag);
    }

    [Fact]
    public void ReminderNotesLine_HasNoHeightCap_ThatWouldCutTallScripts()
    {
        var xaml = Read("Views", "Controls", "ReminderCard.xaml");
        var tag = Regex.Match(xaml, "<TextBlock x:Name=\"NotesText\"[^>]*>").Value;
        Assert.DoesNotContain("MaxHeight", tag);
    }

    [Fact]
    public void EveryTimeOffered_ReadsBackUnchanged_InEveryLanguage()
    {
        var broken = new List<string>();
        foreach (var language in LocalizationService.Available)
        {
            var culture = LocalizationService.KeepTimesUnambiguous(LocalizationService.ForceGregorian(CultureInfo.GetCultureInfo(language.Code)));
            foreach (var time in new[] { new TimeOnly(0, 0), new TimeOnly(9, 0), new TimeOnly(11, 45), new TimeOnly(12, 0), new TimeOnly(15, 30), new TimeOnly(23, 45) })
            {
                var text = time.ToString("t", culture);
                if (!TimeParser.TryParse(text, out var parsed, culture) || parsed != time)
                    broken.Add($"{language.Code}: {time:HH:mm} shown as \"{text}\"");
            }
        }
        Assert.True(broken.Count == 0, "Times that do not read back:\n" + string.Join("\n", broken.Take(60)));
    }
}
