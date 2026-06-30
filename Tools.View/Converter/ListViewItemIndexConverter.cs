using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Tools.View.Converter
{
    public class ListViewItemIndexConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new ListViewItemIndexConverter();

        public object Convert(object value, Type TargetType, object parameter, CultureInfo culture)
        {
            string nullValue = "N/A";
            int indexAtStart = System.Convert.ToInt32(parameter);
            ListViewItem item = (ListViewItem)value;
            if (item == null) return nullValue;

            ListView listView = ItemsControl.ItemsControlFromItemContainer(item) as ListView;
            if (listView == null) return nullValue;

            int index = listView.ItemContainerGenerator.IndexFromContainer(item);
            if (indexAtStart >= 0) index += indexAtStart;
            return index.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
