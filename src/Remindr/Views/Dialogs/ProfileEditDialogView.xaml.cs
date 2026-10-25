using System.Windows;
using System.Windows.Controls;
using Remindr.ViewModels;

namespace Remindr.Views.Dialogs;

public partial class ProfileEditDialogView : UserControl
{
    public ProfileEditDialogView(ProfileEditViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        Remindr.Views.Controls.DigitsOnly.Attach(NewPinBox);
        Remindr.Views.Controls.DigitsOnly.Attach(ConfirmPinBox);
        if (vm.HadPin)
        {
            SetPinCheck.Visibility = Visibility.Collapsed;
            ExistingPinChoices.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };

        // When the view model resets a PIN to "" (e.g. choosing Keep/Remove), clear the box to match.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfileEditViewModel.NewPin) && vm.NewPin.Length == 0 && NewPinBox.Password.Length != 0)
                NewPinBox.Clear();
            else if (e.PropertyName == nameof(ProfileEditViewModel.ConfirmPin) && vm.ConfirmPin.Length == 0 && ConfirmPinBox.Password.Length != 0)
                ConfirmPinBox.Clear();
        };
    }

    private ProfileEditViewModel Vm => (ProfileEditViewModel)DataContext;

    private void NewPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm.NewPin != NewPinBox.Password) Vm.NewPin = NewPinBox.Password;
    }

    private void ConfirmPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm.ConfirmPin != ConfirmPinBox.Password) Vm.ConfirmPin = ConfirmPinBox.Password;
    }

    private void SetPinCheck_Click(object sender, RoutedEventArgs e) =>
        SetAction(SetPinCheck.IsChecked == true ? PinAction.Set : PinAction.Keep);

    private void PinRadio_Click(object sender, RoutedEventArgs e) =>
        SetAction(ChangeRadio.IsChecked == true ? PinAction.Set
                : RemoveRadio.IsChecked == true ? PinAction.Remove : PinAction.Keep);

    private void SetAction(PinAction action)
    {
        Vm.PinAction = action;
        if (action != PinAction.Set) { Vm.NewPin = ""; Vm.ConfirmPin = ""; }
    }
}
