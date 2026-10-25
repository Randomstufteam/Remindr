using System.Windows;
using System.Windows.Controls;
using Remindr.Services;

namespace Remindr.Views.Controls;

/// <summary>A built-in picture (<c>builtin:key</c>) shown at <see cref="Size"/> DIPs that floats gently. See <see cref="Anim.FloatProperty"/>.</summary>
public partial class FloatingPicture : UserControl
{
    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(string), typeof(FloatingPicture), new PropertyMetadata(null, (d, _) => ((FloatingPicture)d).Refresh()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(FloatingPicture), new PropertyMetadata(96.0, (d, _) => ((FloatingPicture)d).Refresh()));

    public FloatingPicture()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>Stored picture value such as <c>builtin:clipboard</c>; unknown values show nothing.</summary>
    public string? Picture { get => (string?)GetValue(PictureProperty); set => SetValue(PictureProperty, value); }

    /// <summary>Width and height in DIPs (default 96).</summary>
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private void Refresh()
    {
        if (Pic is null) return;
        Pic.Width = Pic.Height = Size;
        // Decoded at twice the size so it stays crisp on high-DPI screens.
        Pic.Source = AvatarCatalog.LoadBuiltin(Picture, (int)Math.Ceiling(Size * 2));
    }
}
