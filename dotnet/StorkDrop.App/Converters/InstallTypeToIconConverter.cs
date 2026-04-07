using System.Globalization;
using System.Windows.Data;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Converters;

public sealed class InstallTypeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            InstallType.Plugin => "\uE8F1",
            InstallType.Suite => "\uE8F9",
            InstallType.Bundle => "\uE8B7",
            InstallType.Executable => "\uE945",
            _ => "\uE8F1",
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
