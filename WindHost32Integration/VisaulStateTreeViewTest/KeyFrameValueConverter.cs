using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VisualStateEditor.Converters;

public class KeyFrameValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "null";

        // Handle different value types
        if (value is Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        else if (value is SolidColorBrush brush)
        {
            return $"#{brush.Color.A:X2}{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        }
        else if (value is LinearGradientBrush linearBrush)
        {
            return "LinearGradient with " + linearBrush.GradientStops.Count + " stops";
        }
        else if (value is RadialGradientBrush radialBrush)
        {
            return "RadialGradient with " + radialBrush.GradientStops.Count + " stops";
        }
        else if (value is double doubleValue)
        {
            return doubleValue.ToString("F2");
        }
        else if (value is Thickness thickness)
        {
            return $"{thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom}";
        }
        else if (value is Point point)
        {
            return $"({point.X:F1}, {point.Y:F1})";
        }
        else if (value is Size size)
        {
            return $"{size.Width:F1} × {size.Height:F1}";
        }
        else if (value is Rect rect)
        {
            return $"({rect.X:F1}, {rect.Y:F1}) {rect.Width:F1} × {rect.Height:F1}";
        }
        else if (value is TimeSpan timeSpan)
        {
            return timeSpan.TotalSeconds.ToString("F2") + "s";
        }
        else if (value is bool boolValue)
        {
            return boolValue ? "True" : "False";
        }

        return value.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}