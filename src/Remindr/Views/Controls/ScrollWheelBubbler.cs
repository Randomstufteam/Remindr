using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Remindr.Views.Controls;

/// <summary>
/// WPF's ScrollViewer marks the mouse wheel handled even when it cannot scroll any further (or at all), so a small scroll area nested in
/// a page (a picture grid, a language list, a text box) traps the wheel and touchpad and the page behind it seems stuck. This hands the
/// wheel to the next scroll area up once the innermost one under the pointer cannot move in that direction. Registered once for every
/// ScrollViewer in the app (<see cref="Register"/>), so every present and future page and dialog behaves the same way.
/// </summary>
public static class ScrollWheelBubbler
{
    private static bool _registered;

    /// <summary>Call once at start-up.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    /// <summary>True when a viewer with these numbers can move for this wheel direction (positive delta scrolls up).</summary>
    public static bool CanScroll(int delta, ScrollBarVisibility verticalVisibility, double verticalOffset, double scrollableHeight)
    {
        if (verticalVisibility == ScrollBarVisibility.Disabled || scrollableHeight <= 0) return false;
        return delta > 0 ? verticalOffset > 0 : verticalOffset < scrollableHeight;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not ScrollViewer viewer) return;
        // The preview event tunnels from the outside in: only the innermost viewer under the pointer decides.
        if (Innermost(e.OriginalSource as DependencyObject) != viewer) return;
        if (CanScroll(e.Delta, viewer.VerticalScrollBarVisibility, viewer.VerticalOffset, viewer.ScrollableHeight)) return;

        var parent = ParentElement(viewer);
        if (parent is null) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = viewer,
        });
    }

    private static ScrollViewer? Innermost(DependencyObject? start)
    {
        for (var d = start; d is not null; d = Parent(d))
            if (d is ScrollViewer sv) return sv;
        return null;
    }

    private static UIElement? ParentElement(DependencyObject child)
    {
        for (var d = Parent(child); d is not null; d = Parent(d))
            if (d is UIElement ui) return ui;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
}
