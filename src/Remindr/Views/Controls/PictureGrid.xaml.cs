using System.Windows;
using System.Windows.Controls;
using Remindr.ViewModels;

namespace Remindr.Views.Controls;

/// <summary>One row of thumbnails in a <see cref="PictureGrid"/>.</summary>
public sealed record PictureRow(IReadOnlyList<PictureOption> Items);

/// <summary>
/// Scrollable grid of picture thumbnails, virtualised by rows (items are chunked into rows for a VirtualizingStackPanel,
/// so only visible rows realise buttons and decode images). Give it a fixed Height or let the host constrain it.
/// </summary>
public partial class PictureGrid : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IReadOnlyList<PictureOption>), typeof(PictureGrid),
        new PropertyMetadata(null, (d, _) => ((PictureGrid)d).Rebuild()));

    public static readonly DependencyProperty ThumbSizeProperty = DependencyProperty.Register(
        nameof(ThumbSize), typeof(double), typeof(PictureGrid),
        new PropertyMetadata(52.0, (d, _) => ((PictureGrid)d).Rebuild(force: true)));

    private int _columns;

    public PictureGrid() => InitializeComponent();

    public IReadOnlyList<PictureOption>? ItemsSource
    {
        get => (IReadOnlyList<PictureOption>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Outer size of one thumbnail button in DIPs (an 8px gap is added after it).</summary>
    public double ThumbSize
    {
        get => (double)GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }

    /// <summary>Raised when a thumbnail is clicked or activated by keyboard.</summary>
    public event Action<PictureOption>? PictureChosen;

    private void Thumb_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PictureOption option) PictureChosen?.Invoke(option);
    }

    private void Rows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) Rebuild();
    }

    private void Rebuild(bool force = false)
    {
        if (Rows is null) return;
        var items = ItemsSource;
        var cell = ThumbSize + 8;
        var width = Rows.ActualWidth;
        var columns = width <= 0 ? 7 : Math.Max(1, (int)Math.Floor((width - 14) / cell)); // 14 = scrollbar
        if (!force && columns == _columns && ReferenceEquals(Rows.Tag, items)) return;
        _columns = columns;
        Rows.Tag = items;

        if (items is null || items.Count == 0)
        {
            Rows.ItemsSource = null;
            return;
        }
        var rows = new List<PictureRow>((items.Count + columns - 1) / columns);
        for (var i = 0; i < items.Count; i += columns)
            rows.Add(new PictureRow(items.Skip(i).Take(columns).ToList()));
        Rows.ItemsSource = rows;
    }
}
