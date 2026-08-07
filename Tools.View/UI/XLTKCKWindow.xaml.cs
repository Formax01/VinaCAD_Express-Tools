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
    /// Interaction logic for XLTKCKWindow.xaml
    /// </summary>
    public partial class XLTKCKWindow : Window
    {
        public XLTKCKWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += FindTextView_PreviewKeyDown;
        }

        private void XLTKCKWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure focus is set after layout pass
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var pickButton = this.FindName("btnPickText") as Button;
                if (pickButton != null)
                {
                    pickButton.Focus();
                    Keyboard.Focus(pickButton);
                    // IsDefault is already set in XAML; keep it if needed:
                    pickButton.IsDefault = true;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void FindTextView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                this.Close();
            }
        }
    }
}
