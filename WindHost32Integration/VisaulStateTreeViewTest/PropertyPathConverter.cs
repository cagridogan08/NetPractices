using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisualStateEditor.Converters;

public class PropertyPathConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PropertyPath propertyPath)
        {
            // Handle common WPF property paths
            if (propertyPath.Path != null)
            {
                string path = propertyPath.Path;

                // Clean up common path formats
                path = path.Replace("(", "");
                path = path.Replace(")", "");

                return path;
            }
        }
        return "Unknown Property";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}