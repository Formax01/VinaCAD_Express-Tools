using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace Tools.View.Converter
{
    public class IsLastItemConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataGridRow dataGridRow)
            {
                if (ItemsControl.ItemsControlFromItemContainer(dataGridRow) is DataGrid dataGrid)
                {
                    if (dataGrid.ItemsSource is IEnumerable items)
                    {
                        var currentItem = dataGrid.ItemContainerGenerator.ItemFromContainer(dataGridRow);

                        var objectItems = items.Cast<object>().ToList();

                        return objectItems.IndexOf(currentItem) == objectItems.Count - 1;
                    }
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
