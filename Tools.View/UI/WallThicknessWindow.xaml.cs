using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Tools.ViewModel;

namespace Tools.VinaCAD.UI
{
    public partial class WallThicknessWindow : Window
    {
        private WallThicknessVM ViewModel => (WallThicknessVM)DataContext;

        public double SelectedThickness => ViewModel.SelectedThickness;
        public bool IsPickUpRequested => ViewModel.IsPickUpRequested;

        public WallThicknessWindow(double currentThickness)
        {
            InitializeComponent();
            PreviewKeyDown += Window_PreviewKeyDown;
            DataContext = new WallThicknessVM(currentThickness);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.TryAcceptThickness())
            {
                DialogResult = true;
                return;
            }

            MessageBox.Show("Vui lòng nhập một số hợp lệ lớn hơn 0.", "Lỗi nhập liệu",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtThickness.Focus();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnPickUp_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RequestPickUp();
            DialogResult = true;
        }

        private void TxtThickness_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = Regex.IsMatch(e.Text, "[^0-9.]+");
        }
    }
}
