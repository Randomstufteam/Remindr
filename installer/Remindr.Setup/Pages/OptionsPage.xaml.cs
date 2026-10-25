using System.Windows;
using System.Windows.Controls;
using Remindr.Setup.Core;

namespace Remindr.Setup;

public partial class OptionsPage : UserControl
{
    public OptionsPage(string installDir, ExistingInstall existing, bool desktop, bool startup, bool launch)
    {
        InitializeComponent();
        LocationText.Text = installDir;
        DesktopBox.IsChecked = desktop;
        // Hidden for v0.3 (SetupFeatures.StartWithWindows); the XAML default is Collapsed.
        if (SetupFeatures.StartWithWindows) StartupBox.Visibility = Visibility.Visible;
        StartupBox.IsChecked = SetupFeatures.StartWithWindows && startup;
        LaunchBox.IsChecked = launch;
        if (existing.Mode != InstallMode.Fresh) KeepNote.Visibility = Visibility.Visible;

        Loaded += (_, _) => Motion.Stagger(Heading, LocationLabel, LocationBox, LocationNote, DesktopBox, StartupBox, LaunchBox);
    }

    public bool Desktop => DesktopBox.IsChecked == true;
    public bool Startup => SetupFeatures.StartWithWindows && StartupBox.IsChecked == true;
    public bool Launch => LaunchBox.IsChecked == true;

    public void FocusFirst() => DesktopBox.Focus();
}
