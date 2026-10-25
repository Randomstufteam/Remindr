using System.Globalization;
using System.Windows.Data;
using Remindr.Services;

namespace Remindr.Converters;

/// <summary>'builtin:key' to its (cached, frozen) image decoded at the width in the parameter (default 64 px). Unknown or empty values give null, never an error.</summary>
public sealed class PictureConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = 64;
        if (parameter is not null && int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)) width = w;
        return AvatarCatalog.LoadBuiltin(value as string, width);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
