using System.Windows;
using System.Windows.Input;

namespace Tools.View.UI
{
    public partial class WindowStylePickerWindow : Window
    {
        public WindowStylePickerWindow()
        {
            InitializeComponent();
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        }
    }
}
