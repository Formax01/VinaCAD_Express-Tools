using System;
using System.Windows.Data;

namespace Tools.View.Converter
{
    public class BoolInverterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, 
            System.Globalization.CultureInfo culture)
        {
            return (value is bool boolValue) ? !boolValue : value; 
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            return (value is bool boolValue) ? !boolValue : value;
        }
    }
}
