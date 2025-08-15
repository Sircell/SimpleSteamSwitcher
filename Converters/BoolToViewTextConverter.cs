using System;
using System.Globalization;
using System.Windows.Data;

namespace SimpleSteamSwitcher.Converters
{
    public class BoolToViewTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool showCompactView)
            {
                return showCompactView ? "Card View" : "List View";
            }
            return "List View";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 