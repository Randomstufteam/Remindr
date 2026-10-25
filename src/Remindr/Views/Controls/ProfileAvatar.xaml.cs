using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Remindr.Converters;
using Remindr.Services;

namespace Remindr.Views.Controls;

/// <summary>
/// Round avatar. With a picture (Avatar = 'builtin:name' or 'file:avatars/id.png'): the image clipped to a circle on a glass disc,
/// inside a ring in the profile colour. Otherwise (null, unknown or unreadable): profile colour disc with a white/dark initial.
/// </summary>
public partial class ProfileAvatar : UserControl
{
    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(ProfileAvatar), new PropertyMetadata(40.0, (d, _) => ((ProfileAvatar)d).Refresh()));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ProfileAvatar), new PropertyMetadata("#3B82F6", (d, _) => ((ProfileAvatar)d).Refresh()));

    public static readonly DependencyProperty InitialProperty = DependencyProperty.Register(
        nameof(Initial), typeof(string), typeof(ProfileAvatar), new PropertyMetadata("?", (d, _) => ((ProfileAvatar)d).Refresh()));

    public static readonly DependencyProperty AvatarProperty = DependencyProperty.Register(
        nameof(Avatar), typeof(string), typeof(ProfileAvatar), new PropertyMetadata(null, (d, _) => ((ProfileAvatar)d).Refresh()));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(ProfileAvatar), new PropertyMetadata(3.0, (d, _) => ((ProfileAvatar)d).Refresh()));

    /// <summary>A ready image (e.g. a picked file not saved yet); wins over <see cref="Avatar"/> when set.</summary>
    public static readonly DependencyProperty PreviewImageProperty = DependencyProperty.Register(
        nameof(PreviewImage), typeof(ImageSource), typeof(ProfileAvatar), new PropertyMetadata(null, (d, _) => ((ProfileAvatar)d).Refresh()));

    public ImageSource? PreviewImage { get => (ImageSource?)GetValue(PreviewImageProperty); set => SetValue(PreviewImageProperty, value); }

    public double Diameter { get => (double)GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public string Initial { get => (string)GetValue(InitialProperty); set => SetValue(InitialProperty, value); }
    public string? Avatar { get => (string?)GetValue(AvatarProperty); set => SetValue(AvatarProperty, value); }
    public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }

    private double _dpi;

    public ProfileAvatar()
    {
        InitializeComponent();
        // A picture must never be mirrored (WPF flips images under RightToLeft), and the initial reads the same either way.
        FlowDirection = FlowDirection.LeftToRight;
        Refresh();
        // Before it is in a window GetDpi reports the system DPI; re-decode if the real monitor differs.
        Loaded += (_, _) =>
        {
            if (Math.Abs(VisualTreeHelper.GetDpi(this).DpiScaleX - _dpi) > 0.001) Refresh();
        };
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Refresh();
    }

    private void Refresh()
    {
        if (Disc is null) return;
        Width = Height = Diameter;

        // Decode at exactly the displayed pixel size of the picture (inside ring + 1px gap) so it maps 1:1 and stays crisp.
        var ring = Math.Max(0, RingThickness);
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var px = (int)Math.Ceiling(Math.Max(1, Diameter - 2 * (ring + 1)) * _dpi);
        var image = PreviewImage ?? AvatarCatalog.Load(Avatar, null, px);
        if (image is not null)
        {
            Disc.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "GlassDiscBrush");
            Picture.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            Picture.Margin = new Thickness(ring + 1);
            Picture.Visibility = Visibility.Visible;
            Ring.Stroke = ColorUtil.Brush(Color);
            Ring.StrokeThickness = ring;
            Ring.Margin = new Thickness(ring / 2);
            Ring.Visibility = ring > 0 ? Visibility.Visible : Visibility.Collapsed;
            Letter.Visibility = Visibility.Collapsed;
            return;
        }

        Picture.Visibility = Visibility.Collapsed;
        Ring.Visibility = Visibility.Collapsed;
        Disc.Fill = ColorUtil.Brush(Color);
        Letter.Visibility = Visibility.Visible;
        Letter.Foreground = ColorUtil.ForegroundFor(Color);
        Letter.FontSize = Math.Max(10, Diameter * 0.45);
        Letter.Text = Initial ?? "";
    }
}
