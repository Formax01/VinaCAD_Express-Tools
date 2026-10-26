using System.Windows;
using System.Windows.Input;
using Tools.Model;
using Tools.ViewModel;

namespace Tools.View.UI
{
    public partial class DoorStylePickerWindow : Window
    {
        private DoorStylePickerVM ViewModel => (DoorStylePickerVM)DataContext;
        public DoorStyleModel? SelectedStyle => ViewModel.SelectedStyle;

        public DoorStylePickerWindow(DoorStyleCatalog catalog, DoorStyleModel? initialStyle = null)
        {
            InitializeComponent();
            PreviewKeyDown += Window_PreviewKeyDown;
            DataContext = new DoorStylePickerVM(catalog, initialStyle);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        }
    }
}
