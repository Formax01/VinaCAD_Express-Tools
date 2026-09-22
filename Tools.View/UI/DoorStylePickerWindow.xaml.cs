using System.Windows;
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
            DataContext = new DoorStylePickerVM(catalog, initialStyle);
        }
    }
}
