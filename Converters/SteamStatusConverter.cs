using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpleSteamSwitcher.Converters
{
    public class SteamStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
            {
                if (parameter is string param && param == "color")
                {
                    return isRunning ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(244, 67, 54));
                }
                else
                {
                    return isRunning ? "Steam Running" : "Steam Not Running";
                }
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 