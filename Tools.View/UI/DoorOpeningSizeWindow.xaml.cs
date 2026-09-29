using System.Windows;
using System.Windows.Input;

namespace Tools.View.UI
{
    public partial class DoorOpeningSizeWindow : Window
    {
        public string Request { get; set; } = string.Empty;

        public DoorOpeningSizeWindow()
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
