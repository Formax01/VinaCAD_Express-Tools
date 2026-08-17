using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tools.VinaCAD.UI
{
    public partial class WallThicknessWindow : Window
    {
        public double SelectedThickness { get; private set; }
        public bool IsPickUpRequested { get; private set; } = false;

        private readonly List<double> _defaultThicknesses = new List<double>
        {
            50, 60, 90, 100, 120, 140, 150, 180, 200, 240, 250, 300, 360, 480
        };

        public WallThicknessWindow(double currentThickness)
        {
            InitializeComponent();

            // Nạp dữ liệu vào ListBox
            LstThickness.ItemsSource = _defaultThicknesses;

            // Hiển thị độ dày hiện tại
            TxtThickness.Text = currentThickness.ToString();

            // Bôi đen dòng tương ứng trong ListBox nếu có
            if (_defaultThicknesses.Contains(currentThickness))
            {
                LstThickness.SelectedItem = currentThickness;
            }
        }

        private void LstThickness_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstThickness.SelectedItem != null)
            {
                TxtThickness.Text = LstThickness.SelectedItem.ToString();
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtThickness.Text, out double thickness) && thickness > 0)
            {
                SelectedThickness = thickness;
                DialogResult = true; // Đóng cửa sổ và trả về true
            }
            else
            {
                MessageBox.Show("Vui lòng nhập một số hợp lệ lớn hơn 0.", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtThickness.Focus();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnPickUp_Click(object sender, RoutedEventArgs e)
        {
            // Bật cờ yêu cầu PickUp và đóng cửa sổ để người dùng click trên màn hình CAD
            IsPickUpRequested = true;
            DialogResult = true;
        }

        // Chỉ cho phép nhập số vào TextBox
        private void TxtThickness_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.]+"); // Chi cho phep so va dau cham
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}