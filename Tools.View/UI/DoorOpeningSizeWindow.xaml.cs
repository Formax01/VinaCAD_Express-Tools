using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Tools.Model;

namespace Tools.View.UI
{
    public partial class DoorOpeningSizeWindow : Window
    {
        private static readonly IReadOnlyList<double> HoleWidths = new[]
        {
            600d, 700d, 800d, 900d, 1000d, 1200d, 1400d, 1500d, 1800d, 2100d, 2400d, 3000d
        };
        private static readonly IReadOnlyList<double> PierWidths = new[]
        {
            0d, 100d, 150d, 200d, 250d, 300d, 400d, 500d, 600d, 900d, 1200d
        };

        public double HoleWidth { get; private set; }
        public double PierWidth { get; private set; }
        public double DoorHeight { get; private set; }
        public double WallThickness { get; private set; }
        public bool PlaceAtWallCenter => MiddleWallCheck.IsChecked == true;
        public bool ReverseAlongWall => ReverseAlongWallCheck.IsChecked == true;
        public bool MirrorAcrossWall => MirrorAcrossWallCheck.IsChecked == true;

        public DoorOpeningSizeWindow(
            DoorStyleSelection? initialSelection,
            double defaultWidth,
            double defaultHeight,
            double defaultWallThickness)
        {
            InitializeComponent();
            HoleWidthList.ItemsSource = HoleWidths;
            PierWidthList.ItemsSource = PierWidths;
            double holeWidth = initialSelection?.Width > 0 ? initialSelection.Width : defaultWidth;
            double doorHeight = initialSelection?.Height > 0 ? initialSelection.Height : defaultHeight;
            double wallThickness = initialSelection?.WallThickness > 0
                ? initialSelection.WallThickness
                : defaultWallThickness;
            double pierWidth = initialSelection?.UseEdgeDistance == true ? initialSelection.EdgeDistance : 200;
            HoleWidthText.Text = Format(holeWidth);
            PierWidthText.Text = Format(pierWidth);
            DoorHeightText.Text = Format(doorHeight);
            WallThicknessText.Text = Format(wallThickness);
            HoleWidthList.SelectedItem = holeWidth;
            PierWidthList.SelectedItem = pierWidth;
            MiddleWallCheck.IsChecked = initialSelection?.PlaceAtWallCenter == true;
            ReverseAlongWallCheck.IsChecked = initialSelection?.ReverseAlongWall == true;
            MirrorAcrossWallCheck.IsChecked = initialSelection?.MirrorAcrossWall == true;
            MiddleWallCheck_Changed(this, new RoutedEventArgs());
        }

        private void HoleWidthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HoleWidthList.SelectedItem is double value) HoleWidthText.Text = Format(value);
        }

        private void PierWidthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PierWidthList.SelectedItem is double value) PierWidthText.Text = Format(value);
        }

        private void SameWidth_Click(object sender, RoutedEventArgs e) => PierWidthText.Text = HoleWidthText.Text;

        private void MiddleWallCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = MiddleWallCheck.IsChecked != true;
            PierWidthList.IsEnabled = enabled;
            PierWidthText.IsEnabled = enabled;
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParse(HoleWidthText.Text, out double holeWidth) || holeWidth <= 0)
            {
                ShowValidation("Chiều rộng lỗ cửa phải là số lớn hơn 0.");
                return;
            }
            double pierWidth = 0;
            if (!PlaceAtWallCenter && (!TryParse(PierWidthText.Text, out pierWidth) || pierWidth < 0))
            {
                ShowValidation("Chiều rộng trụ tường phải là số không âm.");
                return;
            }
            if (!TryParse(DoorHeightText.Text, out double doorHeight) || doorHeight <= 0)
            {
                ShowValidation("Chiều cao cửa phải là số lớn hơn 0.");
                return;
            }
            if (!TryParse(WallThicknessText.Text, out double wallThickness) || wallThickness <= 0)
            {
                ShowValidation("Chiều dày tường phải là số lớn hơn 0.");
                return;
            }

            HoleWidth = holeWidth;
            PierWidth = PlaceAtWallCenter ? 0 : pierWidth;
            DoorHeight = doorHeight;
            WallThickness = wallThickness;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ShowValidation(string message)
        {
            MessageBox.Show(this, message, "Thông số chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
