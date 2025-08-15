using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpleSteamSwitcher.Converters
{
    public class AlternatingRowBackgroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] == null || values[1] == null)
                return Brushes.White;

            if (int.TryParse(values[0].ToString(), out int itemIndex) && 
                bool.TryParse(values[1].ToString(), out bool isAlternating))
            {
                if (isAlternating)
                {
                    // Alternate between white and light gray
                    return itemIndex % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 249, 250));
                }
                else
                {
                    // Use a subtle alternating pattern
                    return itemIndex % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(250, 250, 250));
                }
            }

            return Brushes.White;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 