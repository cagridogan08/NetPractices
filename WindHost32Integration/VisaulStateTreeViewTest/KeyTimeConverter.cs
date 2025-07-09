using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace VisualStateEditor.Converters;

public class KeyTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is KeyTime keyTime)
        {
            if (keyTime.Type == KeyTimeType.Paced)
            {
                return "Paced";
            }
            else if (keyTime.Type == KeyTimeType.Percent)
            {
                return $"{keyTime.Percent * 100:F0}%";
            }
            else if (keyTime.Type == KeyTimeType.TimeSpan)
            {
                return $"{keyTime.TimeSpan.TotalSeconds:F2}s";
            }
            else if (keyTime.Type == KeyTimeType.Uniform)
            {
                return "Uniform";
            }
        }

        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}