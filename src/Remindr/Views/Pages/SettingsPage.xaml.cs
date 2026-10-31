using System.Windows;
using System.Windows.Controls;
using Remindr.ViewModels;

namespace Remindr.Views.Pages;

public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        LanguagePicker.Languages = viewModel.Languages;
        Loaded += (_, _) => viewModel.RefreshStartWithWindows();
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e) => LanguagePopup.IsOpen = !LanguagePopup.IsOpen;

    private void LanguagePopup_Opened(object sender, EventArgs e)
    {
        // Opening on the button's own click would toggle the popup straight back shut through the outside-click close.
        LanguageButton.IsHitTestVisible = false;
        // End-align the popup with the button (this offset follows the reading direction, so it also works mirrored).
        LanguagePopup.HorizontalOffset = LanguageButton.ActualWidth - 400;
        LanguagePicker.Reset();
        Dispatcher.BeginInvoke(new Action(LanguagePicker.FocusSearch), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void LanguagePopup_Closed(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => LanguageButton.IsHitTestVisible = true), System.Windows.Threading.DispatcherPriority.Input);
        if (IsLoaded) LanguageButton.Focus();
    }

    private void LanguagePicker_LanguageChosen(string code)
    {
        // The app switches live; the popup closes and the button shows the new name.
        _vm.ChooseLanguage(code);
        LanguagePopup.IsOpen = false;
    }

    private void LanguagePicker_CloseRequested() => LanguagePopup.IsOpen = false;
}
