using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Tools.Model;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;

namespace Tools.View.UI
{
    public partial class StaircaseSectionWindow : Window
    {
        private const int MaxPreviewStoreys = 2;
        private const int MaxPreviewSteps = 60;
        private const double PreviewDrawingHeight = 170.0;
        private const double PreviewMargin = 7.0;
        private const double DimensionOffset = 34.0;
        private const double DimensionTextGap = 8.0;
        private const string InvalidPreviewMessage = "Thông số chưa hợp lệ";
        private static readonly Brush DimensionBrush =
            new SolidColorBrush(Color.FromRgb(51, 165, 28));

        private StaircaseSectionVM? _viewModel;

        public StaircaseSectionWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            base.OnClosed(e);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = e.NewValue as StaircaseSectionVM;
            if (_viewModel != null)
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RefreshPreviews();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RefreshPreviews();
        }

        private void RefreshPreviews()
        {
            if (_viewModel == null) return;

            StaircaseSectionModel settings = _viewModel.Settings;
            RenderPreview(StaircaseSectionType.DoubleFlight,
                DoublePreviewCanvas, DoublePreviewPath, DoublePreviewSummary, settings);
            RenderPreview(StaircaseSectionType.SingleFlight,
                SinglePreviewCanvas, SinglePreviewPath, SinglePreviewSummary, settings);
            RenderPreview(StaircaseSectionType.Scissor,
                ScissorPreviewCanvas, ScissorPreviewPath, ScissorPreviewSummary, settings);
        }

        private void RenderPreview(
            StaircaseSectionType type,
            Canvas canvas,
            Path path,
            TextBlock summary,
            StaircaseSectionModel source)
        {
            // Hình xem trước luôn dùng cấu hình hiện tại nhưng tính riêng cho từng loại thang.
            StaircaseSectionModel preview = source.Copy();
            preview.Type = type;
            if (!preview.TryValidate(out string message))
            {
                ShowPreviewError(canvas, path, summary, message);
                return;
            }

            try
            {
                LimitPreviewSize(preview);
                IReadOnlyList<StaircaseSegment> segments = StaircaseSectionGeometry.Generate(preview);
                path.Data = BuildPreviewGeometry(segments, canvas.Width, preview.StoreyHeight,
                    out Point insertion, out Point storeyTop);
                Ellipse marker = FindInsertMarker(canvas);
                Canvas.SetLeft(marker, insertion.X - marker.Width / 2);
                Canvas.SetTop(marker, insertion.Y - marker.Height / 2);
                marker.Visibility = Visibility.Visible;
                DrawDimensions(canvas, preview, insertion, storeyTop);
                summary.Text = GetPreviewSummary(source, preview);
                summary.ToolTip = GetPreviewToolTip(source, preview);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                ShowPreviewError(canvas, path, summary, exception.Message);
            }
        }

        private static void DrawDimensions(
            Canvas canvas,
            StaircaseSectionModel settings,
            Point insertion,
            Point storeyTop)
        {
            // Đổi tọa độ CAD của tầng đầu tiên sang tọa độ preview để đường DIM bám hình thật.
            Canvas layer = FindDimensionLayer(canvas);
            layer.Children.Clear();
            double scale = (insertion.Y - storeyTop.Y) / settings.StoreyHeight;
            int direction = settings.FirstRunRightward ? 1 : -1;
            double firstRun = (settings.FirstFlightStepNumber - 1) * settings.TreadRun;
            double fullRun = (settings.StepNumber - 1) * settings.TreadRun;
            Point top = ProjectWorld(direction * fullRun, settings.StoreyHeight, insertion, scale);
            Point landing2 = top;

            if (settings.Type == StaircaseSectionType.DoubleFlight)
            {
                landing2 = ProjectWorld(direction * firstRun,
                    settings.FirstFlightStepNumber * settings.CurrentStepHeight, insertion, scale);
                double topX = direction * (firstRun -
                    (settings.StepNumber - settings.FirstFlightStepNumber - 1) * settings.TreadRun);
                top = ProjectWorld(topX, settings.StoreyHeight, insertion, scale);
            }
            else if (settings.Type == StaircaseSectionType.Scissor)
            {
                landing2 = ProjectWorld(direction * fullRun, 0, insertion, scale);
                top = ProjectWorld(0, settings.StoreyHeight, insertion, scale);
            }

            double leftOfDrawing = ((Path)canvas.Children[0]).Data.Bounds.Left - 32;
            DrawVerticalDimension(layer, insertion, top,
                Math.Clamp(leftOfDrawing, 17, canvas.Width - 20), $"H {settings.StoreyHeight:0.####}");
            if (settings.Landing1Width > 0)
            {
                Point outer = ProjectWorld(-direction * settings.Landing1Width, 0, insertion, scale);
                DrawHorizontalDimension(layer, outer, insertion, DimensionOffset,
                    $"Chiếu nghỉ 1: {settings.Landing1Width:0.####}");
            }
            if (settings.Landing2Width > 0)
            {
                Point outer = new Point(landing2.X + direction * settings.Landing2Width * scale,
                    landing2.Y);
                DrawHorizontalDimension(layer, landing2, outer, -DimensionOffset,
                    $"Chiếu nghỉ 2: {settings.Landing2Width:0.####}");
            }
            DrawBeamNotes(layer, settings, insertion, landing2, scale);
        }

        private static Point ProjectWorld(double x, double y, Point insertion, double scale)
        {
            return new Point(insertion.X + x * scale, insertion.Y - y * scale);
        }

        private static void DrawVerticalDimension(
            Canvas layer, Point bottom, Point top, double x, string text)
        {
            // Đường dóng bắt đầu cách vật thể 4 px; đường kích thước nằm ngoài vật thể 32 px.
            AddDimensionLine(layer, new Point(bottom.X - 4, bottom.Y), new Point(x - 4, bottom.Y));
            AddDimensionLine(layer, new Point(top.X - 4, top.Y), new Point(x - 4, top.Y));
            AddDimensionLine(layer, new Point(x, top.Y), new Point(x, bottom.Y));
            AddDimensionTick(layer, new Point(x, top.Y));
            AddDimensionTick(layer, new Point(x, bottom.Y));
            TextBlock note = AddDimensionText(layer, text, x - 18, 0);
            note.LayoutTransform = new RotateTransform(-90);
            note.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(note, (top.Y + bottom.Y - note.DesiredSize.Height) / 2);
        }

        private static void DrawHorizontalDimension(
            Canvas layer, Point first, Point second, double offset, string text)
        {
            double y = first.Y + offset;
            double direction = Math.Sign(offset);
            AddDimensionLine(layer, new Point(first.X, first.Y + direction * 4),
                new Point(first.X, y + direction * 4));
            AddDimensionLine(layer, new Point(second.X, second.Y + direction * 4),
                new Point(second.X, y + direction * 4));
            AddDimensionLine(layer, new Point(first.X, y), new Point(second.X, y));
            AddDimensionTick(layer, new Point(first.X, y));
            AddDimensionTick(layer, new Point(second.X, y));

            // Để chữ ngoài khoảng đo ngắn; không che mặt bậc hay đầu mũi kích thước.
            double x = offset > 0
                ? Math.Clamp(Math.Min(first.X, second.X) - 87, 4, layer.Width - 100)
                : Math.Clamp(Math.Max(first.X, second.X) + 5, 4, layer.Width - 100);
            AddDimensionText(layer, text, x, y + DimensionTextGap);
        }

        private static void DrawBeamNotes(
            Canvas layer, StaircaseSectionModel settings, Point insertion, Point landing2, double scale)
        {
            if (settings.HasBeam1)
            {
                var anchor = new Point(insertion.X, insertion.Y + settings.BeamHeight * scale / 2);
                DrawBeamLeader(layer, anchor, "Dầm 1",
                    Math.Clamp(insertion.X + 18, 100, layer.Width - 55),
                    Math.Clamp(insertion.Y + 15, 170, 184));
            }
            if (settings.HasBeam2)
            {
                int direction = settings.FirstRunRightward ? 1 : -1;
                var anchor = new Point(
                    landing2.X + direction * settings.BeamWidth * scale / 2,
                    landing2.Y + settings.BeamHeight * scale / 2);
                DrawBeamLeader(layer, anchor, "Dầm 2",
                    Math.Clamp(landing2.X + 14, 120, layer.Width - 55),
                    Math.Clamp(landing2.Y + 20, 32, 160));
            }
        }

        private static void DrawBeamLeader(
            Canvas layer, Point anchor, string text, double labelX, double labelY)
        {
            AddDimensionLine(layer, anchor, new Point(labelX + 2, labelY + 6));
            AddDimensionText(layer, text, labelX, labelY);
        }

        private static void AddDimensionLine(Canvas layer, Point first, Point second)
        {
            layer.Children.Add(new Line
            {
                X1 = first.X, Y1 = first.Y, X2 = second.X, Y2 = second.Y,
                Stroke = DimensionBrush, StrokeThickness = 1,
                SnapsToDevicePixels = true
            });
        }

        private static void AddDimensionTick(Canvas layer, Point point)
        {
            AddDimensionLine(layer, new Point(point.X - 3, point.Y + 3),
                new Point(point.X + 3, point.Y - 3));
        }

        private static TextBlock AddDimensionText(Canvas layer, string text, double x, double y)
        {
            var note = new TextBlock
            {
                Text = text, FontSize = 10, Background = Brushes.White,
                VerticalAlignment = VerticalAlignment.Top
            };
            Canvas.SetLeft(note, x);
            Canvas.SetTop(note, y);
            layer.Children.Add(note);
            return note;
        }

        private static Canvas FindDimensionLayer(Canvas canvas)
        {
            foreach (UIElement child in canvas.Children)
            {
                if (child is Canvas layer) return layer;
            }
            throw new InvalidOperationException(InvalidPreviewMessage);
        }

        private static Ellipse FindInsertMarker(Canvas canvas)
        {
            foreach (UIElement child in canvas.Children)
            {
                if (child is Ellipse ellipse) return ellipse;
            }

            throw new InvalidOperationException(InvalidPreviewMessage);
        }

        private static void LimitPreviewSize(StaircaseSectionModel preview)
        {
            // Giới hạn số tầng và bậc được vẽ để hình vẫn đọc được trong khung xem trước.
            preview.StoreyNumber = Math.Min(preview.StoreyNumber, MaxPreviewStoreys);
            if (preview.StepNumber <= MaxPreviewSteps) return;

            double firstFlightRatio = preview.FirstFlightStepNumber / (double)preview.StepNumber;
            preview.StepNumber = MaxPreviewSteps;
            if (preview.Type != StaircaseSectionType.SingleFlight)
            {
                preview.FirstFlightStepNumber = Math.Clamp(
                    (int)Math.Round(firstFlightRatio * MaxPreviewSteps), 1, MaxPreviewSteps - 1);
            }
        }

        private static Geometry BuildPreviewGeometry(
            IReadOnlyList<StaircaseSegment> segments,
            double canvasWidth,
            double storeyHeight,
            out Point insertion,
            out Point storeyTop)
        {
            if (segments.Count == 0) throw new InvalidOperationException(InvalidPreviewMessage);

            // Lấy biên tọa độ thật của hình CAD và co tỷ lệ đồng đều vào khung xem trước.
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (StaircaseSegment segment in segments)
            {
                IncludePoint(segment.Start, ref minX, ref minY, ref maxX, ref maxY);
                IncludePoint(segment.End, ref minX, ref minY, ref maxX, ref maxY);
            }

            double spanX = Math.Max(maxX - minX, double.Epsilon);
            double spanY = Math.Max(maxY - minY, double.Epsilon);
            double scale = Math.Min(
                (canvasWidth - 2 * PreviewMargin) / spanX,
                (PreviewDrawingHeight - 2 * PreviewMargin) / spanY);
            if (!double.IsFinite(scale) || scale <= 0)
                throw new InvalidOperationException(InvalidPreviewMessage);

            double originX = (canvasWidth - spanX * scale) / 2;
            double originY = (PreviewDrawingHeight - spanY * scale) / 2;
            insertion = new Point(originX - minX * scale, originY + maxY * scale);
            storeyTop = new Point(insertion.X, originY + (maxY - storeyHeight) * scale);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                foreach (StaircaseSegment segment in segments)
                {
                    var start = new Point(originX + (segment.Start.X - minX) * scale,
                        originY + (maxY - segment.Start.Y) * scale);
                    var end = new Point(originX + (segment.End.X - minX) * scale,
                        originY + (maxY - segment.End.Y) * scale);
                    context.BeginFigure(start, false, false);
                    context.LineTo(end, true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private static void IncludePoint(
            StaircasePoint point,
            ref double minX,
            ref double minY,
            ref double maxX,
            ref double maxY)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidOperationException(InvalidPreviewMessage);
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        private static string GetPreviewSummary(
            StaircaseSectionModel source,
            StaircaseSectionModel preview)
        {
            string storeys = preview.StoreyNumber < source.StoreyNumber
                ? $"{preview.StoreyNumber}/{source.StoreyNumber} tầng"
                : $"{source.StoreyNumber} tầng";
            string steps = preview.StepNumber < source.StepNumber
                ? $"{preview.StepNumber}/{source.StepNumber} bậc"
                : $"{source.StepNumber} bậc";
            return $"{storeys} · {steps}";
        }

        private static string GetPreviewToolTip(
            StaircaseSectionModel source,
            StaircaseSectionModel preview)
        {
            return $"Hình xem trước: {GetPreviewSummary(source, preview)}\n" +
                $"Cao tầng: {source.StoreyHeight:0.####}; mặt bậc: {source.TreadRun:0.####}\n" +
                $"Chiếu nghỉ: {source.Landing1Width:0.####} / {source.Landing2Width:0.####}\n" +
                $"Bản thang: {source.BoardThickness:0.####}; lan can: {source.RailingHeight:0.####}";
        }

        private static void ShowPreviewError(Canvas canvas, Path path, TextBlock summary, string message)
        {
            path.Data = Geometry.Empty;
            FindDimensionLayer(canvas).Children.Clear();
            FindInsertMarker(canvas).Visibility = Visibility.Collapsed;
            summary.Text = InvalidPreviewMessage;
            summary.ToolTip = message;
        }

        public bool HasValidationErrors()
        {
            return HasValidationErrors(this);
        }

        private static bool HasValidationErrors(DependencyObject element)
        {
            if (Validation.GetHasError(element)) return true;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            {
                if (HasValidationErrors(VisualTreeHelper.GetChild(element, index))) return true;
            }
            return false;
        }
    }
}
