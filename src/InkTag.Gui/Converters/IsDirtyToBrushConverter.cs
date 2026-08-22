using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace InkTag.Gui.Converters;

public class IsDirtyToBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush DarkDirtyBrush = new SolidColorBrush(Color.Parse("#1A3828"));
    private static readonly ISolidColorBrush LightDirtyBrush = new SolidColorBrush(Color.Parse("#E6F4EA"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirty && isDirty)
        {
            if (Application.Current != null &&
                Application.Current.TryGetResource("AppDirtyRowBrush", Application.Current.ActualThemeVariant, out var res) &&
                res is IBrush brush)
            {
                return brush;
            }

            var actualTheme = Application.Current?.ActualThemeVariant;
            return actualTheme == ThemeVariant.Light ? LightDirtyBrush : DarkDirtyBrush;
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
