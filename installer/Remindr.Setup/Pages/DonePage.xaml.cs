using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Remindr.Setup;

/// <summary>Success page for install and uninstall: an animated checkmark, a heading and one detail line.</summary>
public partial class DonePage : UserControl
{
    /// <summary>Update mode: the headline changes since the last release (kept short on purpose).</summary>
    public static readonly string[] NewIn05 =
    {
        "Today page with an overview and filters",
        "Daily summary notification",
        "Remind UI 0.5: a new Today look",
    };

    public DonePage(string heading, string detail, string[]? newItems = null)
    {
        InitializeComponent();
        Heading.Text = heading;
        Detail.Text = detail;
        var lines = new System.Collections.Generic.List<UIElement>();
        if (newItems != null && newItems.Length > 0)
        {
            NewList.Visibility = Visibility.Visible;
            lines.Add(NewTitle);
            CheckGroup.LayoutTransform = new System.Windows.Media.ScaleTransform(0.68, 0.68);   // make room for the list
            Heading.Margin = new Thickness(0, 6, 0, 0);
            NewList.Margin = new Thickness(0, 10, 0, 0);
            foreach (var item in newItems)
            {
                var t = new TextBlock { Text = "•  " + item, FontSize = 14, Margin = new Thickness(0, 3, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush") };
                NewList.Children.Add(t);
                lines.Add(t);
            }
        }
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
                EasingFunction = Motion.EasePop,
            };
            PopScale.ScaleX = PopScale.ScaleY = 0.86;
            PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
            PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
            Motion.Stagger(Heading, Detail);
            if (lines.Count > 0) Motion.Reveal(900, lines.ToArray());
        };
    }
}
