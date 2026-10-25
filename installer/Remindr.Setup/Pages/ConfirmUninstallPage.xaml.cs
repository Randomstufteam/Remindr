using System.Windows;
using System.Windows.Controls;
using Remindr.Setup.Core;

namespace Remindr.Setup;

public partial class ConfirmUninstallPage : UserControl
{
    private readonly string _dataDir;

    public ConfirmUninstallPage(string dataDir)
    {
        InitializeComponent();
        _dataDir = dataDir;
        Body.Text = SetupFeatures.StartWithWindows
            ? "This removes the app, its shortcuts and its Start with Windows entry. Your profiles and reminders are kept unless you tick the box below."
            : "This removes the app and its shortcuts. Your profiles and reminders are kept unless you tick the box below.";
        DataBox_Changed(null, null!);
        Loaded += (_, _) => Motion.Stagger(Heading, Body, DataCard);
    }

    public bool RemoveData => DataBox.IsChecked == true;


    private void DataBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (DataNote == null) return;
        if (RemoveData)
        {
            DataNote.Text = "This moves your profiles, reminders and pictures in " + _dataDir + " to the Recycle Bin. You will be asked to confirm.";
            DataNote.SetResourceReference(TextBlock.ForegroundProperty, "SetupWarnBrush");
        }
        else
        {
            DataNote.Text = "Leave this off to keep your reminders in case you reinstall Remindr later.";
            DataNote.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        }
    }
}
