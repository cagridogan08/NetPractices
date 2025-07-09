using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VisualStateEditor.Converters;

public class StateNameToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stateName)
        {
            switch (stateName.ToLowerInvariant())
            {
                case "normal":
                    return new SolidColorBrush(Colors.LightGreen);
                case "mouseover":
                    return new SolidColorBrush(Colors.LightBlue);
                case "pressed":
                    return new SolidColorBrush(Colors.LightCoral);
                case "disabled":
                    return new SolidColorBrush(Colors.LightGray);
                case "focused":
                    return new SolidColorBrush(Colors.LightYellow);
                default:
                    return new SolidColorBrush(Colors.WhiteSmoke);
            }
        }

        return new SolidColorBrush(Colors.WhiteSmoke);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}