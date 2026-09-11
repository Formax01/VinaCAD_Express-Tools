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
        public bool IsPocketWidth => PocketWidthCheck.IsChecked == true;
        public bool ReverseAlongWall { get; }
        public bool MirrorAcrossWall { get; }
        public bool MeasureHoleRequested { get; private set; }
        public bool MeasurePierRequested { get; private set; }
        public bool PickExistingDoorWidthRequested { get; private set; }

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
            HoleWidth = holeWidth;
            PierWidth = pierWidth;
            DoorHeight = doorHeight;
            WallThickness = wallThickness;
            ReverseAlongWall = initialSelection?.ReverseAlongWall == true;
            MirrorAcrossWall = initialSelection?.MirrorAcrossWall == true;
            HoleWidthText.Text = Format(holeWidth);
            PierWidthText.Text = Format(pierWidth);
            HoleWidthList.SelectedItem = holeWidth;
            PierWidthList.SelectedItem = pierWidth;
            MiddleWallCheck.IsChecked = initialSelection?.PlaceAtWallCenter == true;
            PocketWidthCheck.IsChecked = initialSelection == null || initialSelection.UseEdgeDistance;
            UpdatePierInputs();
        }

        private void HoleWidthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HoleWidthList.SelectedItem is double value) HoleWidthText.Text = Format(value);
        }

        private void PierWidthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PierWidthList.SelectedItem is double value) PierWidthText.Text = Format(value);
        }

        private void PlacementMode_Changed(object sender, RoutedEventArgs e)
        {
            if (sender == MiddleWallCheck && MiddleWallCheck.IsChecked == true)
                PocketWidthCheck.IsChecked = false;
            else if (sender == PocketWidthCheck && PocketWidthCheck.IsChecked == true)
                MiddleWallCheck.IsChecked = false;

            UpdatePierInputs();
        }

        private void UpdatePierInputs()
        {
            bool enabled = IsPocketWidth && !PlaceAtWallCenter;
            PierWidthList.IsEnabled = enabled;
            PierWidthText.IsEnabled = enabled;
            MeasurePierButton.IsEnabled = enabled;
        }

        private void MeasureHole_Click(object sender, RoutedEventArgs e) => RequestMeasurement(true);

        private void MeasurePier_Click(object sender, RoutedEventArgs e) => RequestMeasurement(false);

        private void PickExistingDoorWidth_Click(object sender, RoutedEventArgs e)
        {
            CaptureCurrentValues();
            PickExistingDoorWidthRequested = true;
            DialogResult = true;
        }

        private void RequestMeasurement(bool hole)
        {
            CaptureCurrentValues();
            MeasureHoleRequested = hole;
            MeasurePierRequested = !hole;
            DialogResult = true;
        }

        private void CaptureCurrentValues()
        {
            if (TryParse(HoleWidthText.Text, out double holeWidth) && holeWidth > 0)
                HoleWidth = holeWidth;
            if (TryParse(PierWidthText.Text, out double pierWidth) && pierWidth >= 0)
                PierWidth = pierWidth;
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParse(HoleWidthText.Text, out double holeWidth) || holeWidth <= 0)
            {
                ShowValidation("Chiều rộng lỗ cửa phải là số lớn hơn 0.");
                return;
            }
            double pierWidth = 0;
            if (IsPocketWidth && !PlaceAtWallCenter &&
                (!TryParse(PierWidthText.Text, out pierWidth) || pierWidth < 0))
            {
                ShowValidation("Khoảng cách trụ phải là số không âm.");
                return;
            }

            HoleWidth = holeWidth;
            PierWidth = IsPocketWidth && !PlaceAtWallCenter ? pierWidth : 0;
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
