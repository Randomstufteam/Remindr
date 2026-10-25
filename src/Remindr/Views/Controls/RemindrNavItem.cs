using System.Windows;
using Wpf.Ui.Controls;

namespace Remindr.Views.Controls;

/// <summary>
/// Sidebar item. The NavigationView menu is a plain ItemsControl, so UI Automation shows each item as an
/// "ItemsControlItem" wrapper that only exposes its children (it does not forward the item's own patterns).
/// The template therefore hosts a real Button (PART_InvokeButton) that fills the item: it is the tab stop, carries the
/// item's name and InvokePattern, and raises the item's own click, so keyboard, UIA and mouse share one navigation path.
/// </summary>
public class RemindrNavItem : NavigationViewItem
{
    private System.Windows.Controls.Button? _invoke;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_invoke is not null) _invoke.Click -= OnInvokeClick;
        _invoke = GetTemplateChild("PART_InvokeButton") as System.Windows.Controls.Button;
        if (_invoke is not null) _invoke.Click += OnInvokeClick;
    }

    private void OnInvokeClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OnClick();
    }
}
