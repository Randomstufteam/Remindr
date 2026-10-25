using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Remindr.Setup;

public partial class ErrorPage : UserControl
{
    public ErrorPage(string heading, string message, string? logPath)
    {
        InitializeComponent();
        Heading.Text = heading;
        Message.Text = message;
        LogNote.Text = logPath == null ? "" : "Details were saved to " + logPath;
        LogNote.Visibility = logPath == null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, heading);
        Loaded += (_, _) => Motion.Stagger(Heading, Message, LogNote);
    }
}
