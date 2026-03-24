using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StorkDrop.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is true;
        if (parameter is "Inverse")
            boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        bool isVisible = value is Visibility.Visible;
        if (parameter is "Inverse")
            isVisible = !isVisible;
        return isVisible;
    }
}
