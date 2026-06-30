using System;
using System.Globalization;
using System.Windows.Data;

namespace Tools.View.Converter
{
    public class NumberToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                if (!int.TryParse($"{parameter}", out int number)) return false;
                return count > number;
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
