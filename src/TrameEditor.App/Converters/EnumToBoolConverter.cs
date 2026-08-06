using System.Globalization;
using System.Windows.Data;

namespace TrameEditor.App.Converters;

/// <summary>True se il valore enum corrisponde al parametro; il ConvertBack
/// riporta il valore del parametro (spunta attiva) o il default dell'enum.</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? Enum.Parse(targetType, (string)parameter)
            : Enum.ToObject(targetType, 0);
}
