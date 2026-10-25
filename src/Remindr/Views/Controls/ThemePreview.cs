using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Remindr.Models;

namespace Remindr.Views.Controls;

/// <summary>
/// A tiny drawing of the app window in one theme (rail, title bar, two cards and an accent chip), for the setup's Appearance step.
/// Light and Dark use fixed palettes (they show what that theme looks like whatever the app is using right now);
/// System shows the two side by side, split down the middle. The accent chip follows the live accent.
/// </summary>
public sealed class ThemePreview : UserControl
{
    private sealed record Palette(Color Back, Color Title, Color Rail, Color Card, Color Bar, Color Stroke);

    private static readonly Palette LightPalette = new(
        Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0xE4, 0xE4, 0xE6),
        Colors.White, Color.FromRgb(0xB9, 0xB9, 0xBD), Color.FromArgb(0x33, 0x00, 0x00, 0x00));

    private static readonly Palette DarkPalette = new(
        Color.FromRgb(0x1F, 0x1F, 0x21), Color.FromRgb(0x2A, 0x2A, 0x2C), Color.FromRgb(0x2C, 0x2C, 0x2E),
        Color.FromRgb(0x3A, 0x3A, 0x3D), Color.FromRgb(0x6B, 0x6B, 0x70), Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    public const double PreviewWidth = 132, PreviewHeight = 84;

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(AppTheme), typeof(ThemePreview), new PropertyMetadata(AppTheme.Light, (d, _) => ((ThemePreview)d).Rebuild()));

    public AppTheme Theme
    {
        get => (AppTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public ThemePreview()
    {
        Width = PreviewWidth;
        Height = PreviewHeight;
        Focusable = false;
        IsTabStop = false;
        IsHitTestVisible = false;
        Rebuild();
    }

    private void Rebuild()
    {
        var host = new Grid { Width = PreviewWidth, Height = PreviewHeight, ClipToBounds = true };
        switch (Theme)
        {
            case AppTheme.Dark:
                host.Children.Add(Window(DarkPalette));
                break;
            case AppTheme.System:
                host.Children.Add(Window(LightPalette));
                var dark = Window(DarkPalette);
                dark.Clip = new RectangleGeometry(new Rect(PreviewWidth / 2, 0, PreviewWidth / 2, PreviewHeight));
                host.Children.Add(dark);
                break;
            default:
                host.Children.Add(Window(LightPalette));
                break;
        }
        // Rounded outline over the top so the split and the corners stay clean.
        host.Clip = new RectangleGeometry(new Rect(0, 0, PreviewWidth, PreviewHeight), 8, 8);
        Content = host;
    }

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Grid Window(Palette p)
    {
        var root = new Grid { Width = PreviewWidth, Height = PreviewHeight, Background = Brush(p.Back) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Title bar with three small window buttons.
        var title = new Border { Background = Brush(p.Title) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 5, 0) };
        for (var i = 0; i < 3; i++)
            buttons.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Fill = Brush(p.Bar) });
        title.Child = buttons;
        root.Children.Add(title);

        var body = new Grid();
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rail = new StackPanel { Background = Brush(p.Rail) };
        for (var i = 0; i < 3; i++)
            rail.Children.Add(new Border
            {
                Height = 5, Width = 18, CornerRadius = new CornerRadius(2.5), Background = Brush(p.Bar),
                Margin = new Thickness(6, i == 0 ? 8 : 5, 6, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Opacity = i == 0 ? 1 : 0.55
            });
        body.Children.Add(rail);

        var content = new StackPanel { Margin = new Thickness(8, 8, 8, 0) };
        Grid.SetColumn(content, 1);
        var chip = new Border { Height = 7, Width = 36, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Left };
        chip.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        content.Children.Add(chip);
        for (var i = 0; i < 2; i++)
        {
            var card = new Border
            {
                Height = 17, CornerRadius = new CornerRadius(4), Background = Brush(p.Card), Margin = new Thickness(0, 6, 0, 0),
                BorderBrush = Brush(p.Stroke), BorderThickness = new Thickness(0.5)
            };
            card.Child = new Border
            {
                Height = 4, Width = i == 0 ? 52 : 38, CornerRadius = new CornerRadius(2), Background = Brush(p.Bar),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0)
            };
            content.Children.Add(card);
        }
        body.Children.Add(content);
        root.Children.Add(body);
        return root;
    }
}
