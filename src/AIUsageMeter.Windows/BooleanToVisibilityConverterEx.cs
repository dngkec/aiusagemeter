using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIUsageMeter.Windows;

public sealed class BooleanToVisibilityConverterEx : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
