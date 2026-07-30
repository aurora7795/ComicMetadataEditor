using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace InkTag.Gui.Converters;

public class IsDirtyToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirty && isDirty)
        {
            // Subtle dark yellow-green background to indicate unsaved changes in dark theme
            return new SolidColorBrush(Color.Parse("#2D3016")); 
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
