using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpleSteamSwitcher.Converters
{
    public class TabForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected)
            {
                return isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(102, 102, 102));
            }
            return new SolidColorBrush(Color.FromRgb(102, 102, 102));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 