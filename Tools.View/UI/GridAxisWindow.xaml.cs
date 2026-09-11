using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Tools.Model;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;

namespace Tools.VinaCAD.UI
{
    public partial class GridAxisWindow : Window
    {
        private static readonly Brush PreviewAxisBrush = new SolidColorBrush(Color.FromRgb(235, 82, 82));
        private GridAxisVM ViewModel => (GridAxisVM)DataContext;

        public GridAxisInput? Input { get; private set; }

        public GridAxisWindow(
            string? breadthsText = null,
            string? depthsText = null,
            bool? drawAnnotations = null)
        {
            InitializeComponent();
            DataContext = new GridAxisVM(breadthsText, depthsText, drawAnnotations);
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Loaded += (_, _) => DrawPreview();
            Closed += (_, _) => ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GridAxisVM.BreadthsText)
                || e.PropertyName == nameof(GridAxisVM.DepthsText))
            {
                DrawPreview();
            }
        }

        private void DrawPreview()
        {
            if (PreviewCanvas == null)
                return;

            PreviewCanvas.Children.Clear();

            if (!ViewModel.TryGetPreview(out IReadOnlyList<double> breadths, out IReadOnlyList<double> depths))
                return;

            double width = PreviewCanvas.ActualWidth;
            double height = PreviewCanvas.ActualHeight;
            if (width <= 1 || height <= 1)
                return;

            const double margin = 22;
            double availableWidth = Math.Max(1, width - 2 * margin);
            double availableHeight = Math.Max(1, height - 2 * margin);
            double totalWidth = breadths.Sum();
            double totalDepth = depths.Sum();
            double scale = Math.Min(availableWidth / totalWidth, availableHeight / totalDepth);
            double drawnWidth = totalWidth * scale;
            double drawnHeight = totalDepth * scale;
            double left = (width - drawnWidth) / 2;
            double top = (height - drawnHeight) / 2;

            foreach (double station in GridAxisDataHelper.BuildStations(breadths))
            {
                double x = left + station * scale;
                AddPreviewLine(x, top, x, top + drawnHeight);
            }

            foreach (double station in GridAxisDataHelper.BuildStations(depths))
            {
                double y = top + drawnHeight - station * scale;
                AddPreviewLine(left, y, left + drawnWidth, y);
            }
        }

        private void AddPreviewLine(double x1, double y1, double x2, double y2)
        {
            PreviewCanvas.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = PreviewAxisBrush,
                StrokeThickness = 1.5,
                SnapsToDevicePixels = true
            });
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetDefaults();
            BreadthsTextBox.Focus();
            BreadthsTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.TryBuildInput(out GridAxisInput? input, out string error) || input == null)
            {
                MessageBox.Show(error, "Dữ liệu lưới trục không hợp lệ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Input = input;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawPreview();
        }
    }
}
