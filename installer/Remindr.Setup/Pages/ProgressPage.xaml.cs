using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>Used for both Installing and Removing: a heading, the current step and an animated progress bar.</summary>
public partial class ProgressPage : UserControl
{
    private double _fraction;
    private string _step = "";
    private readonly LinearGradientBrush _sheen;
    private readonly TranslateTransform _sheenMove = new TranslateTransform();

    public ProgressPage(string heading, string firstStep)
    {
        InitializeComponent();
        Heading.Text = heading;
        StepText.Text = firstStep;
        AutomationProperties.SetName(this, heading);

        var accent = Application.Current.TryFindResource("SetupAccentColor") is Color c ? c : Colors.DodgerBlue;
        var light = Color.FromArgb(255, (byte)Math.Min(255, accent.R + 70), (byte)Math.Min(255, accent.G + 70), (byte)Math.Min(255, accent.B + 70));
        _sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0.5, 0), SpreadMethod = GradientSpreadMethod.Repeat,
            RelativeTransform = _sheenMove,
        };
        _sheen.GradientStops.Add(new GradientStop(accent, 0));
        _sheen.GradientStops.Add(new GradientStop(light, 0.5));
        _sheen.GradientStops.Add(new GradientStop(accent, 1));
        Fill.Background = _sheen;

        Loaded += (_, _) =>
        {
            if (Motion.Enabled)
                _sheenMove.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(1400))) { RepeatBehavior = RepeatBehavior.Forever });
            Motion.Stagger(Heading, StepText);
            SetFraction(_fraction, false);
        };
        Track.SizeChanged += (_, _) => SetFraction(_fraction, false);
    }

    public void Report(SetupProgress p)
    {
        if (p.Step != _step)
        {
            _step = p.Step;
            StepText.Text = p.Step + "…";
            if (Motion.Enabled)
                StepText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, Motion.Fast) { EasingFunction = Motion.EaseOut });
        }
        SetFraction(p.Fraction, true);
    }

    public void SetFraction(double f, bool animate)
    {
        _fraction = Math.Max(0, Math.Min(1, f));
        Percent.Text = (int)Math.Round(_fraction * 100) + "%";
        AutomationProperties.SetHelpText(Fill, Percent.Text);
        var target = Track.ActualWidth * _fraction;
        if (animate && Motion.Enabled)
            Fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = Motion.EaseOut });
        else
        {
            Fill.BeginAnimation(WidthProperty, null);
            Fill.Width = target;
        }
    }
}
