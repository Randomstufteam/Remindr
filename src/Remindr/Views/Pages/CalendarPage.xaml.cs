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

        SizeChanged += (_, e) => viewModel.IsNarrow = e.NewSize.Width < NarrowWidth;
        // Pages are singletons: refresh (and replay the entrance) every time the page is shown.
        Loaded += (_, _) =>
        {
            viewModel.SettleDelay = Anim.MotionEnabled ? TimeSpan.FromMilliseconds(450) : TimeSpan.Zero;
            viewModel.IsNarrow = ActualWidth < NarrowWidth;
            viewModel.Reload(animate: true);
        };
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
