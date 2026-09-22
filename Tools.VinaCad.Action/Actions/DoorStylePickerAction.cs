using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.ViewModel;
using Tools.View.UI;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class DoorStylePickerAction
    {
        private static readonly IReadOnlyList<double> HoleWidths = new[]
        {
            600d, 700d, 800d, 900d, 1000d, 1200d, 1400d, 1500d, 1800d, 2100d, 2400d, 3000d
        };

        private static readonly IReadOnlyList<double> PierWidths = new[]
        {
            0d, 100d, 150d, 200d, 250d, 300d, 400d, 500d, 600d, 900d, 1200d
        };

        public DoorStyleSelection CreateDefaultSelection()
        {
            DoorStyleCatalog catalog = DoorStyleCatalogLoader.LoadDefault();
            DoorStyleModel style = catalog.Styles[0];
            return new DoorStyleSelection
            {
                Style = style,
                Width = style.DefaultWidth,
                Height = style.DefaultHeight,
                WallThickness = catalog.DefaultWallThickness > 0 ? catalog.DefaultWallThickness : 200,
                UseEdgeDistance = true,
                EdgeDistance = 200
            };
        }

        public DoorStyleSelection? Execute(DoorStyleSelection? initialSelection = null)
        {
            DoorStyleCatalog catalog = DoorStyleCatalogLoader.LoadDefault();
            DoorStylePickerWindow styleWindow = new DoorStylePickerWindow(catalog, initialSelection?.Style);
            WireStyleWindow(styleWindow);
            Application.ShowModalWindow(styleWindow);
            if (styleWindow.DialogResult != true || styleWindow.SelectedStyle == null) return null;

            return ShowParameters(styleWindow.SelectedStyle, initialSelection, catalog.DefaultWallThickness);
        }

        public DoorStyleSelection? EditParameters(DoorStyleSelection selection)
        {
            return ShowParameters(selection.Style, selection, selection.WallThickness);
        }

        public bool SelectStyle(DoorStyleSelection selection)
        {
            DoorStyleCatalog catalog = DoorStyleCatalogLoader.LoadDefault();
            DoorStylePickerWindow styleWindow = new DoorStylePickerWindow(catalog, selection.Style);
            WireStyleWindow(styleWindow);
            Application.ShowModalWindow(styleWindow);
            if (styleWindow.DialogResult != true || styleWindow.SelectedStyle == null) return false;
            selection.Style = styleWindow.SelectedStyle;
            return true;
        }

        private static DoorStyleSelection? ShowParameters(
            DoorStyleModel style,
            DoorStyleSelection? initialSelection,
            double defaultWallThickness)
        {
            DoorStyleSelection result = new DoorStyleSelection
            {
                Style = style,
                Width = initialSelection?.Width > 0 ? initialSelection.Width : style.DefaultWidth,
                Height = initialSelection?.Height > 0 ? initialSelection.Height : style.DefaultHeight,
                WallThickness = initialSelection?.WallThickness > 0
                    ? initialSelection.WallThickness
                    : (defaultWallThickness > 0 ? defaultWallThickness : 200),
                UseEdgeDistance = initialSelection == null || initialSelection.UseEdgeDistance,
                EdgeDistance = initialSelection?.UseEdgeDistance == true ? initialSelection.EdgeDistance : 200,
                PlaceAtWallCenter = initialSelection?.PlaceAtWallCenter == true,
                ReverseAlongWall = initialSelection?.ReverseAlongWall == true,
                MirrorAcrossWall = initialSelection?.MirrorAcrossWall == true,
                HingeSide = initialSelection?.HingeSide ?? HingeSide.Left,
                OpeningDirection = initialSelection?.OpeningDirection ?? OpeningDirection.Inside
            };

            Document? document = Application.DocumentManager.MdiActiveDocument;
            Editor? editor = document?.Editor;
            while (true)
            {
                DoorOpeningSizeWindow window = CreateParametersWindow(result, style);
                Application.ShowModalWindow(window);
                if (window.DialogResult != true) return null;

                if (window.Request == "PickExistingDoorWidth")
                {
                    if (editor == null || document == null) continue;

                    PromptEntityOptions options = new PromptEntityOptions("\nChọn một cửa của VinaCAD tạo sẵn để lấy độ rộng: ");
                    options.SetRejectMessage("\nĐối tượng được chọn phải là block cửa.");
                    options.AddAllowedClass(typeof(BlockReference), true);
                    PromptEntityResult pickedDoor = editor.GetEntity(options);
                    if (pickedDoor.Status == PromptStatus.OK)
                    {
                        if (OpenDoorHelper.TryGetDoorWidth(document.Database, pickedDoor.ObjectId, out double width))
                            result.Width = Math.Round(width, 2);
                        else
                            editor.WriteMessage("\nBlock được chọn không phải cửa do lệnh AD của VinaCAD tạo.");
                    }
                    continue;
                }

                if (window.Request == "MeasureHole" || window.Request == "MeasurePier")
                {
                    if (editor == null) continue;

                    string prompt = window.Request == "MeasureHole" ? "\nChọn hai điểm đo độ rộng cửa: " : "\nChọn hai điểm đo khoảng cách trụ: ";
                    PromptDoubleResult measured = editor.GetDistance(new PromptDistanceOptions(prompt));
                    if (measured.Status == PromptStatus.OK && measured.Value > 0)
                    {
                        double value = Math.Round(measured.Value, 2);
                        if (window.Request == "MeasureHole")
                            result.Width = value;
                        else
                        {
                            result.EdgeDistance = value;
                            result.UseEdgeDistance = true;
                            result.PlaceAtWallCenter = false;
                        }
                    }
                    continue;
                }

                break;
            }

            /*string placement = result.PlaceAtWallCenter ? "đặt giữa tường" : result.UseEdgeDistance ? $"khoảng trụ={result.EdgeDistance:0.##} mm"  : "đặt theo điểm chọn";

            editor?.WriteMessage($"\nĐã chọn {result.Style.DisplayName}: độ rộng={result.Width:0.##} mm, " +placement + ".");*/

            return result;
        }

        private static DoorOpeningSizeWindow CreateParametersWindow(DoorStyleSelection selection, DoorStyleModel style)
        {
            DoorOpeningSizeWindow window = new DoorOpeningSizeWindow();
            window.HoleWidthList.ItemsSource = HoleWidths;
            window.PierWidthList.ItemsSource = PierWidths;
            double width = selection.Width > 0 ? selection.Width : style.DefaultWidth;
            double pier = selection.UseEdgeDistance ? selection.EdgeDistance : 200;
            window.HoleWidthText.Text = Format(width);
            window.PierWidthText.Text = Format(pier);
            window.HoleWidthList.SelectedItem = width;
            window.PierWidthList.SelectedItem = pier;
            window.MiddleWallCheck.IsChecked = selection.PlaceAtWallCenter;
            window.PocketWidthCheck.IsChecked = selection.UseEdgeDistance && !selection.PlaceAtWallCenter;
            UpdatePierInputs(window);

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
            window.AcceptButton.Click += (_, _) => AcceptParameters(window, selection);
            window.CancelButton.Click += (_, _) => window.DialogResult = false;
            window.MeasureHoleButton.Click += (_, _) => { CaptureCurrentValues(window, selection); window.Request = "MeasureHole"; window.DialogResult = true; };
            window.MeasurePierButton.Click += (_, _) => { CaptureCurrentValues(window, selection); window.Request = "MeasurePier"; window.DialogResult = true; };
            window.ExistingDoorWidthButton.Click += (_, _) => { CaptureCurrentValues(window, selection); window.Request = "PickExistingDoorWidth"; window.DialogResult = true; };
            return window;
        }

        private static void WireStyleWindow(DoorStylePickerWindow window)
        {
            window.StyleList.PreviewMouseLeftButtonUp += (_, args) =>
            {
                DependencyObject? element = args.OriginalSource as DependencyObject;
                while (element != null && element is not ListBoxItem)
                    element = VisualTreeHelper.GetParent(element);
                if (element is not ListBoxItem item || item.DataContext is not DoorStyleModel style) return;
                window.StyleList.SelectedItem = style;
                window.DialogResult = true;
            };
            window.PreviousButton.Click += (_, _) => ((DoorStylePickerVM)window.DataContext).PreviousPage();
            window.NextButton.Click += (_, _) => ((DoorStylePickerVM)window.DataContext).NextPage();
            window.CancelButton.Click += (_, _) => window.DialogResult = false;
            window.PreviewKeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                window.DialogResult = false;
            };
        }

        private static void AcceptParameters(DoorOpeningSizeWindow window, DoorStyleSelection selection)
        {
            if (!TryParse(window.HoleWidthText.Text, out double width) || width <= 0)
            {
                System.Windows.MessageBox.Show(window, "Chiều rộng lỗ cửa phải là số lớn hơn 0.", "Thông số chưa hợp lệ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            double pier = 0;
            bool usePier = window.PocketWidthCheck.IsChecked == true && window.MiddleWallCheck.IsChecked != true;
            if (usePier && (!TryParse(window.PierWidthText.Text, out pier) || pier < 0))
            {
                System.Windows.MessageBox.Show(window, "Khoảng cách trụ phải là số không âm.", "Thông số chưa hợp lệ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            selection.Width = width;
            selection.UseEdgeDistance = usePier;
            selection.EdgeDistance = usePier ? pier : 0;
            selection.PlaceAtWallCenter = window.MiddleWallCheck.IsChecked == true;
            window.DialogResult = true;
        }

        private static void CaptureCurrentValues(DoorOpeningSizeWindow window, DoorStyleSelection selection)
        {
            if (TryParse(window.HoleWidthText.Text, out double width) && width > 0) selection.Width = width;
            if (TryParse(window.PierWidthText.Text, out double pier) && pier >= 0) selection.EdgeDistance = pier;
            selection.UseEdgeDistance = window.PocketWidthCheck.IsChecked == true && window.MiddleWallCheck.IsChecked != true;
            selection.PlaceAtWallCenter = window.MiddleWallCheck.IsChecked == true;
        }

        private static void UpdatePierInputs(DoorOpeningSizeWindow window)
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
    }
}
