using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LANVC.Client.Converters;

public sealed class TabVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string currentTab = value?.ToString() ?? "Home";
        string targetTab = parameter?.ToString() ?? "Home";
        return string.Equals(currentTab, targetTab, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
