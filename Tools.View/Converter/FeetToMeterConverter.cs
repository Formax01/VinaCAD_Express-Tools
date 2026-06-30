using System;
using System.Globalization;
using System.Windows.Data;

namespace Tools.View.Converter
{
    public class FeetToMeterConverter : IValueConverter
    {
        private const double FeetToMeterConversionFactor = 0.3048;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double feetValue)
            {
                double meterValue = feetValue * FeetToMeterConversionFactor;
                return Math.Round(meterValue, 3);
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double.TryParse(value.ToString(), out double doubleValue);
            if (doubleValue is double meterValue)
            {
                double feetValue = meterValue / FeetToMeterConversionFactor;
                return Math.Round(feetValue, 3);
            }

            return value;
        }
    }
}
