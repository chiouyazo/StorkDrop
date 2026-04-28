using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StorkDrop.App.Converters;

public sealed class BadgeColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string hex = value as string ?? "#888888";
        try
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);

            if (parameter is "foreground")
            {
                double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                return new SolidColorBrush(luminance > 140 ? Colors.Black : Colors.White);
            }

            return new SolidColorBrush(color);
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
