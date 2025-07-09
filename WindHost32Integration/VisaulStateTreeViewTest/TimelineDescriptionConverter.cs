using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace VisualStateEditor.Converters;

public class TimelineDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Timeline timeline)
        {
            string targetName = Storyboard.GetTargetName(timeline) ?? "Unknown";
            PropertyPath targetProperty = Storyboard.GetTargetProperty(timeline);
            string propertyName = targetProperty?.Path ?? "Unknown Property";

            // Clean up property path
            propertyName = propertyName.Replace("(", "").Replace(")", "");

            return $"{targetName}.{propertyName}";
        }

        return "Unknown Timeline";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}