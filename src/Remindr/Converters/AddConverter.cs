using System.Globalization;
using System.Windows.Data;

namespace Remindr.Converters;

/// <summary>value + ConverterParameter (a double, invariant culture). Used to size the nav glass to the pane plus its gutter.</summary>
public sealed class AddConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = value is double d ? d : 0.0;
        var add = parameter is double p ? p
            : double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) ? q : 0.0;
        return v + add;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
