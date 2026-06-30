using System;
using System.Collections.Generic;
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

namespace Tools.View.UI
{
    /// <summary>
    /// Interaction logic for DanhSoHieuTKTDWindow.xaml
    /// </summary>
    public partial class DanhSoHieuTKTDWindow : Window
    {
        public DanhSoHieuTKTDWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += FindTextView_PreviewKeyDown;
        }
        private void FindTextView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                this.Close();
            }
        }
        private void BangToaDoCocWindow_Loaded(object sender, RoutedEventArgs e)
        {
            double marginRight = 0;

            Rect workArea = SystemParameters.WorkArea;

            Left = workArea.Right - ActualWidth - marginRight;
            Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
        }
    }
}
