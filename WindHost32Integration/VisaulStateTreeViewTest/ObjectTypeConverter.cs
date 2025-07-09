using System.Globalization;
using System.Windows.Data;

namespace VisualStateEditor.Converters;

public class ObjectTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "Unknown";

        Type type = value.GetType();
        string typeName = type.Name;

        // Make animation names more readable
        if (typeName.Contains("Animation"))
        {
            typeName = typeName.Replace("Animation", " Animation");
            typeName = typeName.Replace("Using", " Using ");

            if (typeName.Contains("KeyFrames"))
            {
                typeName = typeName.Replace("KeyFrames", " KeyFrames");
            }
        }

        return typeName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}