#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Remindr.Services;

namespace Remindr.Views.Dialogs;

/// <summary>
/// Debug-only (<c>Remindr.exe --font-check</c>): every language's native name drawn in that language's fallback font chain, with a note
/// when a character has no glyph in the chain. Look for empty boxes on a stock Windows 11 without extra language packs.
/// </summary>
public sealed class FontCheckWindow : Window
{
    private const string BaseFont = "Segoe UI Variable Text";

    public FontCheckWindow()
    {
        Title = "Remindr font check (Debug)";
        Width = 760;
        Height = 820;
        var list = new StackPanel { Margin = new Thickness(16) };
        var problems = 0;
        foreach (var l in LocalizationService.Supported)
        {
            var missing = FontCoverage.Missing(BaseFont, l.Code, l.NativeName);
            if (missing.Count > 0) problems++;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.Children.Add(new TextBlock { Text = l.Code, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock
            {
                Text = l.NativeName,
                FontSize = 20,
                FontFamily = new FontFamily(FontPolicy.Chain(BaseFont, l.Code)),
                Language = SafeLanguage(l.Code),
                FlowDirection = l.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var note = new TextBlock
            {
                Text = missing.Count == 0 ? l.EnglishName : "MISSING: " + string.Join(" ", missing),
                Foreground = missing.Count == 0 ? Brushes.Gray : Brushes.Red,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(note, 2);
            row.Children.Add(note);
            list.Children.Add(row);
        }
        list.Children.Insert(0, new TextBlock
        {
            Text = problems == 0 ? "No missing glyphs in any language's font chain." : problems + " language(s) have characters no font in the chain can draw.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static XmlLanguage SafeLanguage(string code)
    {
        try { return XmlLanguage.GetLanguage(code); }
        catch (ArgumentException) { return XmlLanguage.GetLanguage("en-US"); }
    }
}
#endif
