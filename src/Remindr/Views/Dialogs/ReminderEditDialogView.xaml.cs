using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Remindr.ViewModels;

namespace Remindr.Views.Dialogs;

public partial class ReminderEditDialogView : UserControl
{
    public ReminderEditDialogView(ReminderEditViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
        vm.Picker.Chosen += _ => PicturePopup.IsOpen = false; // choosing closes the popup (the VM handler sets the icon first)
    }

    private ReminderEditViewModel? Vm => DataContext as ReminderEditViewModel;

    private void PictureButton_Click(object sender, RoutedEventArgs e) => PicturePopup.IsOpen = !PicturePopup.IsOpen;

    private void NoPicture_Click(object sender, RoutedEventArgs e)
    {
        Vm?.ClearIcon();
        PicturePopup.IsOpen = false;
    }

    private void PicturePopup_Opened(object sender, EventArgs e)
    {
        // While open, a click on the button only closes the popup (StaysOpen=False); without this the same click
        // would reach the button afterwards and reopen it.
        PictureButton.IsHitTestVisible = false;
        Dispatcher.BeginInvoke(new Action(Picker.FocusSearch), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void PicturePopup_Closed(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => PictureButton.IsHitTestVisible = true), System.Windows.Threading.DispatcherPriority.Input);
        // Give focus back to the button so keyboard users aren't dropped at the top of the dialog.
        if (IsLoaded && !TitleBox.IsKeyboardFocusWithin) PictureButton.Focus();
    }

    private bool _swallowEscUp;

    /// <summary>True while the picture popup is open.</summary>
    public bool IsPicturePopupOpen => PicturePopup.IsOpen;

    /// <summary>Esc closes just the popup, not the whole dialog. Popup key events route up through the Popup to here.</summary>
    private void Root_PreviewKeyDown(object sender, KeyEventArgs e) => HandleEscDown(e);

    private void Root_PreviewKeyUp(object sender, KeyEventArgs e) => HandleEscUp(e);

    /// <summary>
    /// Called from the owning ContentDialog's own PreviewKeyDown (which sees keys before the dialog's Esc handling, wherever
    /// focus is): while the popup is open Esc only closes the popup.
    /// </summary>
    public void HandleEscDown(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;
        if (!PicturePopup.IsOpen)
        {
            _swallowEscUp = false; // a fresh Esc with the popup closed belongs to the dialog
            return;
        }
        PicturePopup.IsOpen = false;
        _swallowEscUp = true; // the matching key-up lands on the dialog once focus is back on the button
        e.Handled = true;
    }

    public void HandleEscUp(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_swallowEscUp) return;
        _swallowEscUp = false;
        e.Handled = true;
    }
}
