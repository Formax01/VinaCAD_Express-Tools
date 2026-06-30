using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Tools.Model;

namespace Tools.View.UI
{
    /// <summary>
    /// Interaction logic for RenameLayerWindow.xaml
    /// </summary>
    public partial class RenameLayerWindow : Window
    {
        public RenameLayerWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += FindTextView_PreviewKeyDown;
        }

        private void BangToaDoCocWindow_Loaded(object sender, RoutedEventArgs e)
        {
            double marginRight = 0;

            Rect workArea = SystemParameters.WorkArea;

            Left = workArea.Right - ActualWidth - marginRight;
            Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
        }

        private void FindTextView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                this.Close();
            }
        }
        private void dgDanhMuc_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedCells();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                ApplicationCommands.Copy.Execute(null, dgDanhMuc);
                e.Handled = true;
                return;
            }
        }
        private void DeleteSelectedCells()
        {
            foreach (DataGridCellInfo cellInfo in dgDanhMuc.SelectedCells)
            {
                if (cellInfo.Item == null || cellInfo.Column == null)
                    continue;

                string propertyName = cellInfo.Column.SortMemberPath;

                if (string.IsNullOrEmpty(propertyName))
                    continue;

                var property = cellInfo.Item.GetType().GetProperty(propertyName);

                if (property != null && property.CanWrite)
                {
                    property.SetValue(cellInfo.Item, "");
                }
            }

            dgDanhMuc.Items.Refresh();
        }
        private void PasteFromClipboard()
        {
            string text = Clipboard.GetText();

            if (string.IsNullOrWhiteSpace(text))
                return;

            int startRowIndex = dgDanhMuc.Items.IndexOf(dgDanhMuc.CurrentItem);
            int startColIndex = dgDanhMuc.Columns.IndexOf(dgDanhMuc.CurrentColumn);

            if (startRowIndex < 0 || startColIndex < 0)
                return;

            string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // Lấy danh sách đang bind vào DataGrid
            var source = dgDanhMuc.ItemsSource as IList;

            if (source == null)
                return;

            for (int i = 0; i < rows.Length; i++)
            {
                if (string.IsNullOrEmpty(rows[i]))
                    continue;

                string[] cells = rows[i].Split('\t');

                int rowIndex = startRowIndex + i;

                // Nếu paste vượt quá dòng hiện có thì tự thêm dòng mới
                while (rowIndex >= source.Count)
                {
                    source.Add(new RenameTextModel());
                }

                object item = source[rowIndex];

                for (int j = 0; j < cells.Length; j++)
                {
                    int colIndex = startColIndex + j;

                    if (colIndex >= dgDanhMuc.Columns.Count)
                        break;

                    DataGridColumn column = dgDanhMuc.Columns[colIndex];

                    string propertyName = column.SortMemberPath;

                    if (string.IsNullOrEmpty(propertyName))
                        continue;

                    var property = item.GetType().GetProperty(propertyName);

                    if (property != null && property.CanWrite)
                    {
                        object value = ConvertValue(cells[j], property.PropertyType);
                        property.SetValue(item, value);
                    }
                }
            }

            dgDanhMuc.Items.Refresh();
        }
        private object ConvertValue(string text, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (Nullable.GetUnderlyingType(targetType) != null)
                    return null;

                if (targetType == typeof(string))
                    return "";

                return Activator.CreateInstance(targetType);
            }

            Type realType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            CultureInfo viCulture = new CultureInfo("vi-VN");
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;

            try
            {
                if (realType == typeof(string))
                    return text;

                if (realType == typeof(int))
                {
                    if (int.TryParse(text, NumberStyles.Any, viCulture, out int i))
                        return i;

                    if (int.TryParse(text, NumberStyles.Any, invariantCulture, out i))
                        return i;

                    return 0;
                }

                if (realType == typeof(double))
                {
                    if (double.TryParse(text, NumberStyles.Any, viCulture, out double d))
                        return d;

                    if (double.TryParse(text, NumberStyles.Any, invariantCulture, out d))
                        return d;

                    return 0d;
                }

                if (realType == typeof(decimal))
                {
                    if (decimal.TryParse(text, NumberStyles.Any, viCulture, out decimal m))
                        return m;

                    if (decimal.TryParse(text, NumberStyles.Any, invariantCulture, out m))
                        return m;

                    return 0m;
                }

                if (realType == typeof(DateTime))
                {
                    if (DateTime.TryParse(text, viCulture, DateTimeStyles.None, out DateTime date))
                        return date;

                    if (DateTime.TryParse(text, invariantCulture, DateTimeStyles.None, out date))
                        return date;

                    return default(DateTime);
                }

                if (realType == typeof(bool))
                {
                    if (bool.TryParse(text, out bool b))
                        return b;

                    return false;
                }

                return Convert.ChangeType(text, realType, viCulture);
            }
            catch
            {
                if (realType == typeof(string))
                    return text;

                return Activator.CreateInstance(realType);
            }
        }
    }
}
