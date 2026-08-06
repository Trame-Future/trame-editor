using System.Globalization;
using System.Windows.Data;

namespace TrameEditor.App.Converters;

/// <summary>Moltiplica valori double (es. larghezza pagina × zoom).</summary>
public sealed class MultiplyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var result = 1.0;
        foreach (var value in values)
        {
            if (value is not double factor)
                return double.NaN;
            result *= factor;
        }
        return result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
