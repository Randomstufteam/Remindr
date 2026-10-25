using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Remindr.Views.Dialogs;

/// <summary>
/// WPF-UI's ContentDialog template hides the primary button when IsPrimaryButtonEnabled is false but leaves it enabled for
/// UI Automation. This keeps the real template button's IsEnabled in step with a validity check.
/// </summary>
internal static class DialogButtons
{
    public static void BindPrimaryEnabled(ContentDialog dialog, INotifyPropertyChanged source, Func<bool> isValid)
    {
        dialog.IsPrimaryButtonEnabled = isValid();
        PropertyChangedEventHandler onChanged = (_, _) =>
        {
            var valid = isValid();
            dialog.IsPrimaryButtonEnabled = valid;
            Sync(dialog, valid);
        };
        RoutedEventHandler onLoaded = (_, _) => Sync(dialog, isValid());
        source.PropertyChanged += onChanged;
        dialog.Loaded += onLoaded;
        // The view model can outlive the dialog: drop the handlers once it has closed.
        dialog.Closed += (_, _) =>
        {
            source.PropertyChanged -= onChanged;
            dialog.Loaded -= onLoaded;
        };
    }

    public static void Sync(ContentDialog dialog, bool enabled)
    {
        // WPF-UI 3.0.5's template names no button parts; its buttons are identified by Command = TemplateButtonCommand and
        // CommandParameter = ContentDialogButton. Text matching is only the fallback (it could also hit a button in Content).
        var buttons = FindButtons(dialog).ToList();
        var primary = buttons.Where(b => b.CommandParameter is ContentDialogButton.Primary
                                         && ReferenceEquals(b.Command, dialog.TemplateButtonCommand)).ToList();
        if (primary.Count == 0)
        {
            var text = dialog.PrimaryButtonText;
            if (string.IsNullOrEmpty(text)) return;
            primary = buttons.Where(b => b.Content as string == text).ToList();
        }
        foreach (var b in primary) b.IsEnabled = enabled;
    }

    private static IEnumerable<ButtonBase> FindButtons(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase b) yield return b;
            foreach (var d in FindButtons(child)) yield return d;
        }
    }
}
