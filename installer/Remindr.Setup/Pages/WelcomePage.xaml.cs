using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Remindr.Setup.Core;

namespace Remindr.Setup;

public partial class WelcomePage : UserControl
{
    private readonly ScaleTransform LogoScale;

    public WelcomePage(ExistingInstall existing)
    {
        InitializeComponent();
        LogoScale = new ScaleTransform(1, 1);
        Logo.RenderTransform = LogoScale;
        Heading.Text = "Remindr " + SetupInfo.ShortVersion;

        if (existing.Mode != InstallMode.Fresh)
        {
            UpdateNote.Visibility = Visibility.Visible;
            UpdateText.Text = existing.Mode switch
            {
                InstallMode.Update => "Remindr " + existing.InstalledVersion + " is installed. This will update it and keep your reminders.",
                InstallMode.Reinstall => "Remindr " + SetupInfo.Version + " is already installed. This will reinstall it and keep your reminders.",
                _ => "A newer Remindr (" + existing.InstalledVersion + ") is installed. This will replace it with " + SetupInfo.Version + " and keep your reminders.",
            };
        }

        Loaded += (_, _) =>
        {
            if (Motion.Enabled)
            {
                LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, Pop());
                LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, Pop());
            }
            Motion.Stagger(Heading, Tagline, Row1, Row2, Row3, Row4);
        };
    }

    private static DoubleAnimation Pop() => new DoubleAnimation(0.8, 1, new Duration(System.TimeSpan.FromMilliseconds(450)))
    {
        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
    };
}


