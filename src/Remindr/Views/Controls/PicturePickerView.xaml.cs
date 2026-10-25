using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Remindr.ViewModels;

namespace Remindr.Views.Controls;

/// <summary>Category chips, a search box (150 ms debounce) and a <see cref="PictureGrid"/>. DataContext = PicturePickerViewModel.</summary>
public partial class PicturePickerView : UserControl
{
    public static readonly DependencyProperty GridHeightProperty = DependencyProperty.Register(
        nameof(GridHeight), typeof(double), typeof(PicturePickerView),
        new PropertyMetadata(200.0, (d, e) => ((PicturePickerView)d).Pictures.Height = (double)e.NewValue));

    public static readonly DependencyProperty ThumbSizeProperty = DependencyProperty.Register(
        nameof(ThumbSize), typeof(double), typeof(PicturePickerView),
        new PropertyMetadata(52.0, (d, e) => ((PicturePickerView)d).Pictures.ThumbSize = (double)e.NewValue));

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public PicturePickerView()
    {
        InitializeComponent();
        Pictures.Height = GridHeight;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (Vm is { } vm) vm.Search = SearchBox.Text.Trim();
        };
        Unloaded += (_, _) => _debounce.Stop();
    }

    public double GridHeight
    {
        get => (double)GetValue(GridHeightProperty);
        set => SetValue(GridHeightProperty, value);
    }

    public double ThumbSize
    {
        get => (double)GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }

    public void FocusSearch() => SearchBox.Focus();

    /// <summary>Empties the search box (the view model's search is reset by whoever replaces it).</summary>
    public void ResetSearch()
    {
        _debounce.Stop();
        SearchBox.Text = "";
    }

    private PicturePickerViewModel? Vm => DataContext as PicturePickerViewModel;

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ((FrameworkElement)sender).DataContext is PickerChip chip) vm.SelectChip(chip);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Grid_PictureChosen(PictureOption option) => Vm?.Choose(option);
}

