using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrameEditor.App.Converters;

/// <summary>Visible se il valore non è null, altrimenti Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
