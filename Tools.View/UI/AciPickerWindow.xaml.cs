using System.Windows;
using Tools.ViewModel;

namespace Tools.View.UI
{
    public partial class AciPickerWindow : Window
    {
        private AciPickerVM ViewModel => (AciPickerVM)DataContext;

        public short? Selected => ViewModel.SelectedAci;

        public AciPickerWindow(short current)
        {
            InitializeComponent();
            DataContext = new AciPickerVM(current);
        }
    }

}
