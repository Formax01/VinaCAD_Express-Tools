using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WpfBrush = System.Windows.Media.Brush;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfGeometry = System.Windows.Media.Geometry;
using WpfLine = System.Windows.Shapes.Line;
using WpfPath = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Tools.View.UI;
using Tools.ViewModel;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public sealed class StaircaseSectionAction
    {
        private const string CompletionMessageFormat = "\nLTP: Đã tạo mặt cắt cầu thang gồm {0} đường.";
        private const string ErrorMessageFormat = "Lỗi LTP: {0}";

        public void Execute()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            try
            {
                StaircaseSectionModel initialSettings = StaircaseSectionSetting.CreateInitial();
                var settingsStore = new StaircaseSectionSettingsStore();
                StaircaseSectionModel storedSettings = settingsStore.LoadOrDefault(initialSettings);

                var dialogService = new StaircaseSectionDialogService(document.Editor);
                StaircaseSectionModel? settings = dialogService.Show(
                    storedSettings, initialSettings);
                if (settings == null) return;
                settingsStore.Save(settings);

                var insertionJig = new StaircaseSectionInsertionJig(settings);
                PromptPointResult pointResult = (PromptPointResult)document.Editor.Drag(insertionJig);
                if (pointResult.Status != PromptStatus.OK) return;

                int lineCount = new StaircaseSectionDrawingService().Draw(
                    document.Database, insertionJig.InsertionPoint, settings);

                // B3: Cập nhật màn hình và trả kết quả cho người dùng.
                document.Editor.UpdateScreen();
                document.Editor.WriteMessage(string.Format(CompletionMessageFormat, lineCount));
            }
            catch (Exception exception)
            {
                Logger.Info(nameof(StaircaseSectionAction), exception);
                MessageBox.Show(
                    string.Format(ErrorMessageFormat, exception.Message),
                    StringDefinition.TITLE_ERROR);
            }
        }
    }

    internal sealed class StaircaseSectionInsertionJig : DrawJig
    {
        private const short PrimaryColorIndex = 7;
        private const short SecondaryColorIndex = 2;
        private const string InsertionPointPrompt = "\nChọn điểm chèn mặt cắt cầu thang: ";
        private readonly IReadOnlyList<StaircaseSegment> _segments;
        private Point3d _insertionPoint = Point3d.Origin;

        public StaircaseSectionInsertionJig(StaircaseSectionModel settings)
        {
            _segments = StaircaseSectionGeometry.Generate(settings);
        }

        public Point3d InsertionPoint => _insertionPoint;

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions(InsertionPointPrompt)
            {
                UserInputControls = UserInputControls.Accept3dCoordinates
            };
            PromptPointResult result = prompts.AcquirePoint(options);
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (result.Value == _insertionPoint) return SamplerStatus.NoChange;

            _insertionPoint = result.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            foreach (StaircaseSegment segment in _segments)
            {
                draw.SubEntityTraits.Color = segment.Style == StaircaseSegmentStyle.Secondary
                    ? SecondaryColorIndex
                    : PrimaryColorIndex;
                draw.Geometry.WorldLine(
                    ToPoint3d(segment.Start), ToPoint3d(segment.End));
            }

            return true;
        }

        private Point3d ToPoint3d(StaircasePoint point)
        {
            return new Point3d(
                _insertionPoint.X + point.X,
                _insertionPoint.Y + point.Y,
                _insertionPoint.Z);
        }
    }

    internal sealed class StaircaseSectionDialogService
    {
        private const double MeasurementTolerance = 1e-9;
        private const string NumberFormatMessage = "Vui lòng nhập đúng định dạng số cho tất cả thông số.";
        private const string StoreyHeightPrompt = "\nChọn hai điểm đo chiều cao tầng: ";
        private const string Landing1Prompt = "\nChọn hai điểm đo chiều rộng chiếu nghỉ 1: ";
        private const string Landing2Prompt = "\nChọn hai điểm đo chiều rộng chiếu nghỉ 2: ";
        private const string DefaultMeasurementPrompt = "\nChọn hai điểm đo khoảng cách: ";

        private readonly Editor _editor;

        public StaircaseSectionDialogService(Editor editor)
        {
            _editor = editor;
        }

        public StaircaseSectionModel? Show( StaircaseSectionModel settings, StaircaseSectionModel initialSettings)
        {
            StaircaseSectionModel currentSettings = settings;

            while (true)
            {
                var viewModel = new StaircaseSectionVM(currentSettings);
                StaircaseSectionWindow view = CreateView(viewModel, initialSettings);
                using (var preview = new StaircaseSectionPreviewController(view, viewModel))
                {
                    Application.ShowModalWindow(view);
                }
                currentSettings = viewModel.Settings;

                if (view.DialogResult != true) return null;
                if (viewModel.IsAccepted) return currentSettings;

                TryApplyMeasurement(currentSettings, viewModel.MeasurementRequest);
            }
        }

        private static StaircaseSectionWindow CreateView( StaircaseSectionVM viewModel, StaircaseSectionModel initialSettings)
        {
            var view = new StaircaseSectionWindow { DataContext = viewModel };

            viewModel.AcceptCmd = new RelayCommand(() => Accept(view, viewModel));
            viewModel.CancelCmd = new RelayCommand(() => view.DialogResult = false);
            viewModel.ResetCmd = new RelayCommand(
                () => viewModel.Reset(initialSettings.Copy()));

            viewModel.MeasureStoreyHeightCmd = CreateMeasurementCommand(
                view, viewModel, StaircaseMeasurement.StoreyHeight);
            viewModel.MeasureLanding1WidthCmd = CreateMeasurementCommand(
                view, viewModel, StaircaseMeasurement.Landing1Width);
            viewModel.MeasureLanding2WidthCmd = CreateMeasurementCommand(
                view, viewModel, StaircaseMeasurement.Landing2Width);
            return view;
        }

        private static RelayCommand CreateMeasurementCommand( Window view, StaircaseSectionVM viewModel, StaircaseMeasurement measurement)
        {
            return new RelayCommand(() => RequestMeasurement(view, viewModel, measurement));
        }

        private static void Accept(StaircaseSectionWindow view, StaircaseSectionVM viewModel)
        {
            if (StaircaseSectionPreviewController.HasValidationErrors(view))
            {
                MessageBox.Show(NumberFormatMessage, StringDefinition.TITLE_MESSAGE);
                return;
            }

            if (!StaircaseSectionValidator.TryValidate(
                viewModel.Settings, out string message))
            {
                MessageBox.Show(message, StringDefinition.TITLE_MESSAGE);
                return;
            }

            viewModel.IsAccepted = true;
            view.DialogResult = true;
        }

        private static void RequestMeasurement( Window view, StaircaseSectionVM viewModel, StaircaseMeasurement measurement)
        {
            viewModel.MeasurementRequest = measurement;
            view.DialogResult = true;
        }

        private void TryApplyMeasurement( StaircaseSectionModel settings, StaircaseMeasurement measurement)
        {
            var options = new PromptDistanceOptions(GetMeasurementPrompt(measurement));
            PromptDoubleResult result = _editor.GetDistance(options);
            if (result.Status != PromptStatus.OK || result.Value <= MeasurementTolerance) return;

            switch (measurement)
            {
                case StaircaseMeasurement.StoreyHeight:
                    settings.StoreyHeight = result.Value;
                    break;
                case StaircaseMeasurement.Landing1Width:
                    settings.Landing1Width = result.Value;
                    break;
                case StaircaseMeasurement.Landing2Width:
                    settings.Landing2Width = result.Value;
                    break;
            }
        }

        private static string GetMeasurementPrompt(StaircaseMeasurement measurement)
        {
            return measurement switch
            {
                StaircaseMeasurement.StoreyHeight => StoreyHeightPrompt,
                StaircaseMeasurement.Landing1Width => Landing1Prompt,
                StaircaseMeasurement.Landing2Width => Landing2Prompt,
                _ => DefaultMeasurementPrompt
            };
        }
    }

    internal sealed class StaircaseSectionPreviewController : IDisposable
    {
        private const int MaxPreviewStoreys = 2;
        private const int MaxPreviewSteps = 60;
        private const double PreviewDrawingHeight = 170.0;
        private const double PreviewMargin = 7.0;
        private const double DimensionOffset = 34.0;
        private const double DimensionTextGap = 8.0;
        private const string InvalidPreviewMessage = "Thông số chưa hợp lệ";
        private static readonly WpfBrush DimensionBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 165, 28));

        private readonly StaircaseSectionWindow _view;
        private readonly StaircaseSectionVM _viewModel;

        public StaircaseSectionPreviewController(StaircaseSectionWindow view, StaircaseSectionVM viewModel)
        {
            _view = view;
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RefreshPreviews();
        }

        public void Dispose()
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        public static bool HasValidationErrors(DependencyObject element)
        {
            if (Validation.GetHasError(element)) return true;
            for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); index++)
            {
                if (HasValidationErrors(System.Windows.Media.VisualTreeHelper.GetChild(element, index))) return true;
            }
            return false;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RefreshPreviews();
        }

        private void RefreshPreviews()
        {
            StaircaseSectionModel settings = _viewModel.Settings;
            RenderPreview(StaircaseSectionType.DoubleFlight, "Double", settings);
            RenderPreview(StaircaseSectionType.SingleFlight, "Single", settings);
            RenderPreview(StaircaseSectionType.Scissor, "Scissor", settings);
        }

        private void RenderPreview(StaircaseSectionType type, string prefix, StaircaseSectionModel source)
        {
            Canvas canvas = FindElement<Canvas>($"{prefix}PreviewCanvas");
            WpfPath path = FindElement<WpfPath>($"{prefix}PreviewPath");
            TextBlock summary = FindElement<TextBlock>($"{prefix}PreviewSummary");
            StaircaseSectionModel preview = source.Copy();
            preview.Type = type;
            if (!StaircaseSectionValidator.TryValidate(preview, out string message))
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
                WpfEllipse marker = FindInsertMarker(canvas);
                Canvas.SetLeft(marker, insertion.X - marker.Width / 2);
                Canvas.SetTop(marker, insertion.Y - marker.Height / 2);
                marker.Visibility = System.Windows.Visibility.Visible;
                DrawDimensions(canvas, preview, insertion, storeyTop);
                summary.Text = GetPreviewSummary(source, preview);
                summary.ToolTip = GetPreviewToolTip(source, preview);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                ShowPreviewError(canvas, path, summary, exception.Message);
            }
        }

        private T FindElement<T>(string name) where T : FrameworkElement
        {
            return _view.FindName(name) as T
                ?? throw new InvalidOperationException(InvalidPreviewMessage);
        }

        private static void DrawDimensions(Canvas canvas, StaircaseSectionModel settings, Point insertion, Point storeyTop)
        {
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

            double leftOfDrawing = ((WpfPath)canvas.Children[0]).Data.Bounds.Left - 32;
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
                Point outer = new Point(landing2.X + direction * settings.Landing2Width * scale, landing2.Y);
                DrawHorizontalDimension(layer, landing2, outer, -DimensionOffset,
                    $"Chiếu nghỉ 2: {settings.Landing2Width:0.####}");
            }
            DrawBeamNotes(layer, settings, insertion, landing2, scale);
        }

        private static Point ProjectWorld(double x, double y, Point insertion, double scale)
        {
            return new Point(insertion.X + x * scale, insertion.Y - y * scale);
        }

        private static void DrawVerticalDimension(Canvas layer, Point bottom, Point top, double x, string text)
        {
            AddDimensionLine(layer, new Point(bottom.X - 4, bottom.Y), new Point(x - 4, bottom.Y));
            AddDimensionLine(layer, new Point(top.X - 4, top.Y), new Point(x - 4, top.Y));
            AddDimensionLine(layer, new Point(x, top.Y), new Point(x, bottom.Y));
            AddDimensionTick(layer, new Point(x, top.Y));
            AddDimensionTick(layer, new Point(x, bottom.Y));
            TextBlock note = AddDimensionText(layer, text, x - 18, 0);
            note.LayoutTransform = new System.Windows.Media.RotateTransform(-90);
            note.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(note, (top.Y + bottom.Y - note.DesiredSize.Height) / 2);
        }

        private static void DrawHorizontalDimension(Canvas layer, Point first, Point second, double offset, string text)
        {
            double y = first.Y + offset;
            double direction = Math.Sign(offset);
            AddDimensionLine(layer, new Point(first.X, first.Y + direction * 4), new Point(first.X, y + direction * 4));
            AddDimensionLine(layer, new Point(second.X, second.Y + direction * 4), new Point(second.X, y + direction * 4));
            AddDimensionLine(layer, new Point(first.X, y), new Point(second.X, y));
            AddDimensionTick(layer, new Point(first.X, y));
            AddDimensionTick(layer, new Point(second.X, y));
            double x = offset > 0
                ? Math.Clamp(Math.Min(first.X, second.X) - 87, 4, layer.Width - 100)
                : Math.Clamp(Math.Max(first.X, second.X) + 5, 4, layer.Width - 100);
            AddDimensionText(layer, text, x, y + DimensionTextGap);
        }

        private static void DrawBeamNotes(Canvas layer, StaircaseSectionModel settings, Point insertion, Point landing2, double scale)
        {
            if (settings.HasBeam1)
            {
                var anchor = new Point(insertion.X, insertion.Y + settings.BeamHeight * scale / 2);
                DrawBeamLeader(layer, anchor, "Dầm 1", Math.Clamp(insertion.X + 18, 100, layer.Width - 55), Math.Clamp(insertion.Y + 15, 170, 184));
            }
            if (settings.HasBeam2)
            {
                int direction = settings.FirstRunRightward ? 1 : -1;
                var anchor = new Point(landing2.X + direction * settings.BeamWidth * scale / 2, landing2.Y + settings.BeamHeight * scale / 2);
                DrawBeamLeader(layer, anchor, "Dầm 2", Math.Clamp(landing2.X + 14, 120, layer.Width - 55), Math.Clamp(landing2.Y + 20, 32, 160));
            }
        }

        private static void DrawBeamLeader(Canvas layer, Point anchor, string text, double labelX, double labelY)
        {
            AddDimensionLine(layer, anchor, new Point(labelX + 2, labelY + 6));
            AddDimensionText(layer, text, labelX, labelY);
        }

        private static void AddDimensionLine(Canvas layer, Point first, Point second)
        {
            layer.Children.Add(new WpfLine { X1 = first.X, Y1 = first.Y, X2 = second.X, Y2 = second.Y, Stroke = DimensionBrush, StrokeThickness = 1, SnapsToDevicePixels = true });
        }

        private static void AddDimensionTick(Canvas layer, Point point)
        {
            AddDimensionLine(layer, new Point(point.X - 3, point.Y + 3), new Point(point.X + 3, point.Y - 3));
        }

        private static TextBlock AddDimensionText(Canvas layer, string text, double x, double y)
        {
            var note = new TextBlock { Text = text, FontSize = 10, Background = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Top };
            Canvas.SetLeft(note, x);
            Canvas.SetTop(note, y);
            layer.Children.Add(note);
            return note;
        }

        private static Canvas FindDimensionLayer(Canvas canvas)
        {
            foreach (UIElement child in canvas.Children)
                if (child is Canvas layer) return layer;
            throw new InvalidOperationException(InvalidPreviewMessage);
        }

        private static WpfEllipse FindInsertMarker(Canvas canvas)
        {
            foreach (UIElement child in canvas.Children)
                if (child is WpfEllipse ellipse) return ellipse;
            throw new InvalidOperationException(InvalidPreviewMessage);
        }

        private static void LimitPreviewSize(StaircaseSectionModel preview)
        {
            preview.StoreyNumber = Math.Min(preview.StoreyNumber, MaxPreviewStoreys);
            if (preview.StepNumber <= MaxPreviewSteps) return;

            double firstFlightRatio = preview.FirstFlightStepNumber / (double)preview.StepNumber;
            preview.StepNumber = MaxPreviewSteps;
            if (preview.Type != StaircaseSectionType.SingleFlight)
                preview.FirstFlightStepNumber = Math.Clamp((int)Math.Round(firstFlightRatio * MaxPreviewSteps), 1, MaxPreviewSteps - 1);
        }

        private static WpfGeometry BuildPreviewGeometry(IReadOnlyList<StaircaseSegment> segments, double canvasWidth, double storeyHeight, out Point insertion, out Point storeyTop)
        {
            if (segments.Count == 0) throw new InvalidOperationException(InvalidPreviewMessage);

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (StaircaseSegment segment in segments)
            {
                IncludePoint(segment.Start, ref minX, ref minY, ref maxX, ref maxY);
                IncludePoint(segment.End, ref minX, ref minY, ref maxX, ref maxY);
            }

            double spanX = Math.Max(maxX - minX, double.Epsilon);
            double spanY = Math.Max(maxY - minY, double.Epsilon);
            double scale = Math.Min((canvasWidth - 2 * PreviewMargin) / spanX, (PreviewDrawingHeight - 2 * PreviewMargin) / spanY);
            if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException(InvalidPreviewMessage);

            double originX = (canvasWidth - spanX * scale) / 2;
            double originY = (PreviewDrawingHeight - spanY * scale) / 2;
            insertion = new Point(originX - minX * scale, originY + maxY * scale);
            storeyTop = new Point(insertion.X, originY + (maxY - storeyHeight) * scale);
            var geometry = new System.Windows.Media.StreamGeometry();
            using (System.Windows.Media.StreamGeometryContext context = geometry.Open())
            {
                foreach (StaircaseSegment segment in segments)
                {
                    var start = new Point(originX + (segment.Start.X - minX) * scale, originY + (maxY - segment.Start.Y) * scale);
                    var end = new Point(originX + (segment.End.X - minX) * scale, originY + (maxY - segment.End.Y) * scale);
                    context.BeginFigure(start, false, false);
                    context.LineTo(end, true, false);
                }
            }
            geometry.Freeze();
            return geometry;
        }

        private static void IncludePoint(StaircasePoint point, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new InvalidOperationException(InvalidPreviewMessage);
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        private static string GetPreviewSummary(StaircaseSectionModel source, StaircaseSectionModel preview)
        {
            string storeys = preview.StoreyNumber < source.StoreyNumber ? $"{preview.StoreyNumber}/{source.StoreyNumber} tầng" : $"{source.StoreyNumber} tầng";
            string steps = preview.StepNumber < source.StepNumber ? $"{preview.StepNumber}/{source.StepNumber} bậc" : $"{source.StepNumber} bậc";
            return $"{storeys} · {steps}";
        }

        private static string GetPreviewToolTip(StaircaseSectionModel source, StaircaseSectionModel preview)
        {
            return $"Hình xem trước: {GetPreviewSummary(source, preview)}\n" +
                $"Cao tầng: {source.StoreyHeight:0.####}; mặt bậc: {source.TreadRun:0.####}\n" +
                $"Chiếu nghỉ: {source.Landing1Width:0.####} / {source.Landing2Width:0.####}\n" +
                $"Bản thang: {source.BoardThickness:0.####}; lan can: {source.RailingHeight:0.####}";
        }

        private static void ShowPreviewError(Canvas canvas, WpfPath path, TextBlock summary, string message)
        {
            path.Data = WpfGeometry.Empty;
            FindDimensionLayer(canvas).Children.Clear();
            FindInsertMarker(canvas).Visibility = System.Windows.Visibility.Collapsed;
            summary.Text = InvalidPreviewMessage;
            summary.ToolTip = message;
        }
    }

    internal sealed class StaircaseSectionDrawingService
    {
        private const short PrimaryColorIndex = 7;
        private const short SecondaryColorIndex = 2;
        private const short ByLayerColorIndex = 256;
        private const string GroupDictionaryKey = "*A";
        private const string GroupDescription = "Mặt cắt cầu thang VinaCAD LTP";
        private const string InvalidLayerNameMessage = "Tên layer mặt cắt cầu thang không hợp lệ.";

        public int Draw( Database database, Point3d insertionPoint, StaircaseSectionModel settings)
        {
            // B1: Sinh và kiểm tra hình học trước khi mở transaction ghi.
            IReadOnlyList<StaircaseSegment> segments = StaircaseSectionGeometry.Generate(settings);

            // B2: Ghi toàn bộ đường và Group trong cùng một transaction nguyên tử.
            using Transaction transaction = database.TransactionManager.StartTransaction();
            BlockTableRecord modelSpace = OpenModelSpace(database, transaction);
            ObjectIdCollection entityIds = DrawSegments(
                database, transaction, modelSpace, insertionPoint, segments);

            if (settings.CreateGroup && entityIds.Count > 0)
            {
                CreateGroup(database, transaction, entityIds);
            }

            // B3: Commit và trả số đối tượng đã tạo.
            transaction.Commit();
            return entityIds.Count;
        }

        private static BlockTableRecord OpenModelSpace( Database database, Transaction transaction)
        {
            var blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }

        private static ObjectIdCollection DrawSegments( Database database, Transaction transaction, BlockTableRecord modelSpace, Point3d insertionPoint, IReadOnlyList<StaircaseSegment> segments)
        {
            ObjectId primaryLayerId = GetOrCreateSectionLayer(
                database, transaction, StaircaseSectionSetting.LayerName, PrimaryColorIndex);
            ObjectId secondaryLayerId = GetOrCreateSectionLayer(
                database, transaction, StaircaseSectionSetting.SecondaryLayerName, SecondaryColorIndex);
            var entityIds = new ObjectIdCollection();
            foreach (StaircaseSegment segment in segments)
            {
                ObjectId layerId = segment.Style == StaircaseSegmentStyle.Secondary
                    ? secondaryLayerId
                    : primaryLayerId;
                Line line = CreateLine(database, insertionPoint, segment, layerId);
                modelSpace.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
                entityIds.Add(line.ObjectId);
            }

            return entityIds;
        }

        private static Line CreateLine( Database database, Point3d insertionPoint, StaircaseSegment segment, ObjectId layerId)
        {
            var line = new Line(
                ToPoint3d(insertionPoint, segment.Start),
                ToPoint3d(insertionPoint, segment.End));
            line.SetDatabaseDefaults(database);
            line.LayerId = layerId;
            line.ColorIndex = ByLayerColorIndex;
            return line;
        }

        private static ObjectId GetOrCreateSectionLayer( Database database, Transaction transaction, string layerName, short colorIndex)
        {
            // B1: Kiểm tra tên layer đã khai báo tập trung trong Setting.
            if (string.IsNullOrWhiteSpace(layerName))
                throw new InvalidOperationException(InvalidLayerNameMessage);

            // B2: Dùng lại layer hiện có và đồng bộ màu theo cấu hình LTP.
            var layers = (LayerTable)transaction.GetObject(
                database.LayerTableId, OpenMode.ForRead);
            if (layers.Has(layerName))
            {
                ObjectId existingId = layers[layerName];
                var existing = (LayerTableRecord)transaction.GetObject(
                    existingId, OpenMode.ForWrite);
                existing.Color = Teigha.Colors.Color.FromColorIndex(
                    Teigha.Colors.ColorMethod.ByAci, colorIndex);
                return existingId;
            }

            // B3: Tạo layer mới với màu ACI tương ứng.
            layers.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = layerName,
                Color = Teigha.Colors.Color.FromColorIndex(
                    Teigha.Colors.ColorMethod.ByAci, colorIndex)
            };
            ObjectId layerId = layers.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }

        private static Point3d ToPoint3d(Point3d insertionPoint, StaircasePoint point)
        {
            return new Point3d(
                insertionPoint.X + point.X,
                insertionPoint.Y + point.Y,
                insertionPoint.Z);
        }

        private static void CreateGroup( Database database, Transaction transaction, ObjectIdCollection entityIds)
        {
            var groups = (DBDictionary)transaction.GetObject(
                database.GroupDictionaryId, OpenMode.ForWrite);
            var group = new Group(GroupDescription, true);
            groups.SetAt(GroupDictionaryKey, group);
            transaction.AddNewlyCreatedDBObject(group, true);
            group.Append(entityIds);
        }
    }
}
