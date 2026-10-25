using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Remindr.Services;
using Remindr.ViewModels;

namespace Remindr.Views.Controls;

/// <summary>Picture chooser: category chips + search over the shared picture library, "Your own picture..." and "Initial only". DataContext = AvatarChooserViewModel.</summary>
public partial class AvatarChooserView : UserControl
{
    public AvatarChooserView() => InitializeComponent();

    private AvatarChooserViewModel? Vm => DataContext as AvatarChooserViewModel;

    private void InitialOnly_Click(object sender, RoutedEventArgs e) => Vm?.UseInitialOnly();

    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("Avatar_ChooseTitle"),
            Filter = Loc.Get("Avatar_FileFilterName") + " (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) vm.TryUpload(dialog.FileName);
    }
}
