using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Remindr.ViewModels;
using Remindr.Views.Controls;

namespace Remindr.Views.Pages;

public partial class CalendarPage : Page
{
    private const double NarrowWidth = 900;

    private readonly CalendarViewModel _viewModel;

    public CalendarPage(CalendarViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // The grid mirrors under RightToLeft (the week starts on the right), so the Left arrow moves to the cell on the
        // left, which is the next day. The XAML key bindings assume left-to-right; this handler swaps them there.
        MonthGrid.PreviewKeyDown += (_, e) =>
        {
            if (MonthGrid.FlowDirection != FlowDirection.RightToLeft || e.Key is not (Key.Left or Key.Right)) return;
            viewModel.MoveSelectionCommand.Execute(e.Key == Key.Left ? "Right" : "Left");
            e.Handled = true;
        };


        // Remind UI 0.4: month / week slide, title cross-fade, sliding segment pill.
        viewModel.Navigating += OnNavigating;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CalendarViewModel.Title) && IsLoaded) Anim.FadeSwap(TitleText);
            else if (e.PropertyName == nameof(CalendarViewModel.Mode)) PlacePill(animate: IsLoaded);
        };
        SegmentGrid.SizeChanged += (_, _) => PlacePill(animate: false);
        // The outgoing copy is only kept while it slides away; once collapsed, drop its items.
        MonthGridOut.IsVisibleChanged += (_, _) => { if (!MonthGridOut.IsVisible) MonthGridOut.ItemsSource = null; };
        WeekColumnsOut.IsVisibleChanged += (_, _) => { if (!WeekColumnsOut.IsVisible) WeekColumnsOut.ItemsSource = null; };
        SizeChanged += (_, e) => viewModel.IsNarrow = e.NewSize.Width < NarrowWidth;
        // Pages are singletons: refresh (and replay the entrance) every time the page is shown.
        Loaded += (_, _) =>
        {
            viewModel.SettleDelay = Anim.MotionEnabled ? TimeSpan.FromMilliseconds(450) : TimeSpan.Zero;
            viewModel.IsNarrow = ActualWidth < NarrowWidth;
            viewModel.Reload(animate: true);
            PlacePill(animate: false);
        };
    }


    // ---- Remind UI 0.4 motion --------------------------------------------------------------------------

    /// <summary>The selection pill sits behind the checked segment: one third of the segment grid wide, TranslateX by index.</summary>
    private void PlacePill(bool animate)
    {
        var cell = SegmentGrid.ActualWidth / 3;
        if (cell <= 0) return;
        SegmentPill.Width = cell;
        Anim.SlidePill(SegmentPill, (int)_viewModel.Mode, cell, animate);
    }

    /// <summary>
    /// Previous / Next / Today / arrow keys that change the month or week: keep the old cells as a second, non-interactive list that
    /// slides out while the freshly loaded one slides in (Anim.SlideSwap). Real visuals, so right-to-left comes out right. Gate off: nothing kept.
    /// </summary>
    private void OnNavigating(int direction)
    {
        if (!IsLoaded || !Anim.EffectsEnabled) return;
        if (_viewModel.IsMonth && MonthGrid.IsVisible)
        {
            Anim.Rest(MonthGrid);
            Anim.Rest(MonthGridOut);
            MonthGridOut.ItemsSource = _viewModel.MonthCells.ToList();
            Anim.SlideSwap(MonthGridOut, MonthGrid, direction);
        }
        else if (_viewModel.IsWeek && WeekColumnsList.IsVisible)
        {
            foreach (var item in _viewModel.WeekColumns.SelectMany(c => c.Items)) item.Animate = false; // the copies must not fade in again
            Anim.Rest(WeekColumnsList);
            Anim.Rest(WeekColumnsOut);
            WeekColumnsOut.ItemsSource = _viewModel.WeekColumns.ToList();
            Anim.SlideSwap(WeekColumnsOut, WeekColumnsList, direction);
        }
    }
    // Mouse routing only: the view model decides what a click or double-click means.

    private void OnDayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarDayViewModel day }) return;
        MonthGrid.Focus();   // so the arrow keys work right after a click
        if (e.ClickCount >= 2) _viewModel.NewReminderOnCommand.Execute(day.Date);
        else _viewModel.SelectDayCommand.Execute(day.Date);
        e.Handled = true;
    }

    // UIA Invoke (or any Click not preceded by our preview handler): select, same as a single click.
    private void OnDayInvoke(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarDayViewModel day }) return;
        MonthGrid.Focus();
        _viewModel.SelectDayCommand.Execute(day.Date);
    }

    private void OnWeekHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarWeekColumnViewModel column }) return;
        if (e.ClickCount >= 2) _viewModel.NewReminderOnCommand.Execute(column.Date);
        else _viewModel.SelectDayCommand.Execute(column.Date);
        e.Handled = true;
    }

    private void OnWeekHeaderInvoke(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarWeekColumnViewModel column })
            _viewModel.SelectDayCommand.Execute(column.Date);
    }

    private void OnWeekColumnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarWeekColumnViewModel column }) return;
        // Clicks on a card belong to the card; only the empty part of the column counts.
        if (IsInsideCard(e.OriginalSource as DependencyObject, sender)) return;
        if (e.ClickCount >= 2) _viewModel.NewReminderOnCommand.Execute(column.Date);
        else _viewModel.SelectDayCommand.Execute(column.Date);
    }

    private static bool IsInsideCard(DependencyObject? element, object stopAt)
    {
        while (element is not null && !ReferenceEquals(element, stopAt))
        {
            if (element is ReminderCard) return true;
            element = element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
