using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpleSteamSwitcher.Converters
{
    public class TabBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected)
            {
                return isSelected ? new SolidColorBrush(Color.FromRgb(173, 216, 230)) : Brushes.Transparent;
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PasswordStatusBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasPassword)
            {
                // Dark green for accounts with password, dark yellow for accounts without password
                return hasPassword 
                    ? new SolidColorBrush(Color.FromRgb(25, 135, 84))   // Dark green (#198754)
                    : new SolidColorBrush(Color.FromRgb(181, 137, 0));  // Dark yellow (#B58900)
            }
            return new SolidColorBrush(Color.FromRgb(108, 117, 125)); // Gray fallback
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 