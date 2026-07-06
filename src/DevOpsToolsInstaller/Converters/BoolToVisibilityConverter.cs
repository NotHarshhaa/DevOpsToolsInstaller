using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DevOpsToolsInstaller.Converters;

/// <summary>
/// Converts a <see cref="bool"/> to <see cref="Visibility"/> for XAML
/// <c>{Binding}</c> expressions (WinUI 3 has no built-in equivalent).
/// Pass <c>ConverterParameter=Invert</c> to reverse the mapping.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility v && v == Visibility.Visible;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;

        return visible;
    }
}
