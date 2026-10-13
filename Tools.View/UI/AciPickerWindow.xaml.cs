using System.Windows;
using Tools.ViewModel;

namespace Tools.View.UI
{
    public partial class AciPickerWindow : Window
    {
        public AciPickerWindow(AciPickerVM viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}