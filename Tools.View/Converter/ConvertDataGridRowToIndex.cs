using System;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;

namespace Tools.View.Converter
{
    public class ConvertDataGridRowToIndex : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataGridRow row)
                return row.GetIndex() + 1;
            else
                return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
