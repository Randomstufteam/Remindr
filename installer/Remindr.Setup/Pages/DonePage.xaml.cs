using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Remindr.Setup;

/// <summary>Success page for install and uninstall: an animated checkmark, a heading and one detail line.</summary>
public partial class DonePage : UserControl
{
    public DonePage(string heading, string detail)
    {
        InitializeComponent();
        Heading.Text = heading;
        Detail.Text = detail;
        AutomationProperties.SetName(this, heading);
        AutomationProperties.SetName(CheckGroup, "Success");

        Loaded += (_, _) =>
        {
            if (!Motion.Enabled) return;          // static, fully drawn check
            Ring.StrokeDashOffset = 52.4;
            Tick.StrokeDashOffset = 10.6;
            Ring.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(52.4, 0, new Duration(TimeSpan.FromMilliseconds(520))) { EasingFunction = Motion.EaseOut });
            Tick.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(10.6, 0, new Duration(TimeSpan.FromMilliseconds(360))) { BeginTime = TimeSpan.FromMilliseconds(440), EasingFunction = Motion.EaseOut });

            var pop = new DoubleAnimation(0.86, 1, new Duration(TimeSpan.FromMilliseconds(520)))
            {
                BeginTime = TimeSpan.FromMilliseconds(380),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
            };
            PopScale.ScaleX = PopScale.ScaleY = 0.86;
            PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
            PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
            Motion.Stagger(Heading, Detail);
        };
    }
}
