using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.View.UI;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class ArrangeWindowAction
    {
        public void Execute()
        {
            try
            {
                Document? document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;

                WindowStylePickerAction picker = new WindowStylePickerAction();
                WindowStyleSelection selection = picker.CreateDefaultSelection();

                Editor editor = document.Editor;
                int createdCount = 0;
                while (true)
                {
                    PromptEntityOptions options = CreateWallPrompt();
                    PromptEntityResult result = editor.GetEntity(options);
                    if (result.Status == PromptStatus.Keyword)
                    {
                        HandleActiveKeyword(editor, picker, result.StringResult, ref selection);
                        continue;
                    }
                    if (result.Status != PromptStatus.OK) break;

                    ObjectId windowId;
                    try
                    {
                        windowId = WindowOpeningHelper.CreateOpening(
                            document.Database,
                            result.ObjectId,
                            result.PickedPoint,
                            selection);
                        createdCount++;
                        editor.UpdateScreen();
                    }
                    catch (Exception exception)
                    {
                        editor.WriteMessage($"\nAW: {exception.Message} Hãy chọn lại.");
                        continue;
                    }
                }

                editor.WriteMessage($"\nAW: đã tạo {createdCount} cửa sổ.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Không thể lấy Editor: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }


        public sealed class WindowOpeningSizeResult
        {
            public double Width { get; init; }
            public double Height { get; init; }
            public double WallThickness { get; init; }
            public double EdgeDistance { get; init; }
            public bool UseEdgeDistance { get; init; }
            public bool PlaceAtWallCenter { get; init; }
            public bool ReverseAlongWall { get; init; }
            public bool MirrorAcrossWall { get; init; }
            public bool MeasureHoleRequested { get; init; }
            public bool MeasurePierRequested { get; init; }
            public bool PickExistingWindowWidthRequested { get; init; }
        }

        private static readonly IReadOnlyList<double> HoleWidths = new[]
        {
            600d, 700d, 800d, 900d, 1000d, 1200d, 1400d, 1500d, 1800d, 2100d, 2400d, 3000d
        };

        private static readonly IReadOnlyList<double> PierWidths = new[]
        {
            0d, 100d, 150d, 200d, 250d, 300d, 400d, 500d, 600d, 900d, 1200d
        };

        public WindowOpeningSizeResult? ShowOpeningSize(
            WindowStyleSelection? initialSelection,
            double defaultWidth,
            double defaultHeight,
            double defaultWallThickness)
        {
            WindowOpeningSizeWindow window = new WindowOpeningSizeWindow();
            window.HoleWidthList.ItemsSource = HoleWidths;
            window.PierWidthList.ItemsSource = PierWidths;

            double width = initialSelection?.Width > 0 ? initialSelection.Width : defaultWidth;
            double height = initialSelection?.Height > 0 ? initialSelection.Height : defaultHeight;
            double wallThickness = initialSelection?.WallThickness > 0 ? initialSelection.WallThickness : defaultWallThickness;
            double edgeDistance = initialSelection?.UseEdgeDistance == true ? initialSelection.EdgeDistance : 200;
            window.HoleWidthText.Text = Format(width);
            window.PierWidthText.Text = Format(edgeDistance);
            window.HoleWidthList.SelectedItem = width;
            window.PierWidthList.SelectedItem = edgeDistance;
            window.MiddleWallCheck.IsChecked = initialSelection?.PlaceAtWallCenter == true;
            window.PocketWidthCheck.IsChecked = initialSelection == null || initialSelection.UseEdgeDistance;

            bool measureHole = false;
            bool measurePier = false;
            bool pickExisting = false;
            window.HoleWidthList.SelectionChanged += (_, _) =>
            {
                if (window.HoleWidthList.SelectedItem is double value) window.HoleWidthText.Text = Format(value);
            };
            window.PierWidthList.SelectionChanged += (_, _) =>
            {
                if (window.PierWidthList.SelectedItem is double value) window.PierWidthText.Text = Format(value);
            };
            RoutedEventHandler placementChanged = (_, _) =>
            {
                if (window.MiddleWallCheck.IsChecked == true) window.PocketWidthCheck.IsChecked = false;
                else if (window.PocketWidthCheck.IsChecked == true) window.MiddleWallCheck.IsChecked = false;
                UpdatePierInputs(window);
            };
            window.MiddleWallCheck.Checked += placementChanged;
            window.MiddleWallCheck.Unchecked += placementChanged;
            window.PocketWidthCheck.Checked += placementChanged;
            window.PocketWidthCheck.Unchecked += placementChanged;
            window.MeasureHoleButton.Click += (_, _) => { measureHole = true; CaptureText(window, ref width, ref edgeDistance); window.DialogResult = true; };
            window.MeasurePierButton.Click += (_, _) => { measurePier = true; CaptureText(window, ref width, ref edgeDistance); window.DialogResult = true; };
            window.ExistingWindowWidthButton.Click += (_, _) => { pickExisting = true; CaptureText(window, ref width, ref edgeDistance); window.DialogResult = true; };
            window.AcceptButton.Click += (_, _) =>
            {
                if (!TryParse(window.HoleWidthText.Text, out double parsedWidth) || parsedWidth <= 0)
                {
                    System.Windows.MessageBox.Show(window, "Chiều rộng lỗ cửa sổ phải là số lớn hơn 0.", "Thông số chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (window.PocketWidthCheck.IsChecked == true && window.MiddleWallCheck.IsChecked != true &&
                    (!TryParse(window.PierWidthText.Text, out double parsedEdge) || parsedEdge < 0))
                {
                    System.Windows.MessageBox.Show(window, "Khoảng cách trụ phải là số không âm.", "Thông số chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                width = parsedWidth;
                edgeDistance = window.PocketWidthCheck.IsChecked == true && window.MiddleWallCheck.IsChecked != true &&
                                TryParse(window.PierWidthText.Text, out double validEdge) ? validEdge : 0;
                window.DialogResult = true;
            };
            window.CancelButton.Click += (_, _) => window.DialogResult = false;
            UpdatePierInputs(window);
            Application.ShowModalWindow(window);
            if (window.DialogResult != true) return null;

            return new WindowOpeningSizeResult
            {
                Width = width,
                Height = height,
                WallThickness = wallThickness,
                EdgeDistance = edgeDistance,
                UseEdgeDistance = window.PocketWidthCheck.IsChecked == true && window.MiddleWallCheck.IsChecked != true,
                PlaceAtWallCenter = window.MiddleWallCheck.IsChecked == true,
                ReverseAlongWall = initialSelection?.ReverseAlongWall == true,
                MirrorAcrossWall = initialSelection?.MirrorAcrossWall == true,
                MeasureHoleRequested = measureHole,
                MeasurePierRequested = measurePier,
                PickExistingWindowWidthRequested = pickExisting
            };
        }

        private static void CaptureText(WindowOpeningSizeWindow window, ref double width, ref double edgeDistance)
        {
            if (TryParse(window.HoleWidthText.Text, out double parsedWidth) && parsedWidth > 0) width = parsedWidth;
            if (TryParse(window.PierWidthText.Text, out double parsedEdge) && parsedEdge >= 0) edgeDistance = parsedEdge;
        }

        private static void UpdatePierInputs(WindowOpeningSizeWindow window)
        {
            bool enabled = window.PocketWidthCheck.IsChecked == true && window.MiddleWallCheck.IsChecked != true;
            window.PierWidthList.IsEnabled = enabled;
            window.PierWidthText.IsEnabled = enabled;
            window.MeasurePierButton.IsEnabled = enabled;
        }

        private static bool TryParse(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        private static PromptEntityOptions CreateWallPrompt()
        {
            PromptEntityOptions options = new PromptEntityOptions("Chọn tường hoặc [S-Chọn cửa sổ/W-Cài đặt thông số/C-Center] <Enter: Kết thúc>: ")
            {
                AllowNone = true
            };
            options.SetRejectMessage("\nChỉ chấp nhận Line, Polyline thẳng hoặc block tường.");
            options.AddAllowedClass(typeof(Line), true);
            options.AddAllowedClass(typeof(Polyline), true);
            options.AddAllowedClass(typeof(BlockReference), true);
            options.Keywords.Add("Style", "S", "S");
            options.Keywords.Add("Parameters", "W", "W");
            options.Keywords.Add("Center", "C", "C");

            return options;
        }

        private static void HandleActiveKeyword(
            Editor editor,
            WindowStylePickerAction picker,
            string keyword,
            ref WindowStyleSelection selection)
        {
            switch (keyword.ToUpperInvariant())
            {
                case "STYLE":
                case "A":
                    picker.SelectStyle(selection);
                    break;
                case "PARAMETERS":
                case "W":
                    WindowStyleSelection? changed = picker.EditParameters(selection);
                    if (changed != null) selection = changed;
                    break;
                case "CENTER":
                case "C":
                    selection.PlaceAtWallCenter = true;
                    selection.UseEdgeDistance = false;
                    editor.WriteMessage("\n Đã chuyển vị trí cửa sổ về giữa tường.");
                    break;
            }
        }
    }
}
