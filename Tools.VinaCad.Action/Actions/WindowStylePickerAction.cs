using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class WindowStylePickerAction
    {
        public WindowStyleSelection CreateDefaultSelection()
        {
            WindowStyleCatalog catalog = WindowStyleCatalogLoader.LoadDefault();
            WindowStyleModel style = catalog.Styles[0];
            return new WindowStyleSelection
            {
                Style = style,
                Width = style.DefaultWidth,
                Height = style.DefaultHeight,
                WallThickness = catalog.DefaultWallThickness > 0 ? catalog.DefaultWallThickness : 200,
                UseEdgeDistance = true,
                EdgeDistance = 200
            };
        }

        public WindowStyleSelection? Execute(WindowStyleSelection? initialSelection = null)
        {
            WindowStyleCatalog catalog = WindowStyleCatalogLoader.LoadDefault();
            WindowStylePickerWindow styleWindow = CreateStyleWindow(catalog, initialSelection?.Style, out WindowStylePickerVM viewModel);
            Application.ShowModalWindow(styleWindow);
            if (styleWindow.DialogResult != true || viewModel.SelectedStyle == null) return null;

            return ShowParameters(viewModel.SelectedStyle, initialSelection, catalog.DefaultWallThickness);
        }

        public WindowStyleSelection? EditParameters(WindowStyleSelection selection)
        {
            return ShowParameters(selection.Style, selection, selection.WallThickness);
        }

        public bool SelectStyle(WindowStyleSelection selection)
        {
            WindowStyleCatalog catalog = WindowStyleCatalogLoader.LoadDefault();
            WindowStylePickerWindow styleWindow = CreateStyleWindow(catalog, selection.Style, out WindowStylePickerVM viewModel);
            Application.ShowModalWindow(styleWindow);
            if (styleWindow.DialogResult != true || viewModel.SelectedStyle == null) return false;
            selection.Style = viewModel.SelectedStyle;
            return true;
        }

        private static WindowStylePickerWindow CreateStyleWindow(
            WindowStyleCatalog catalog,
            WindowStyleModel? initialStyle,
            out WindowStylePickerVM viewModel)
        {
            WindowStylePickerWindow window = new WindowStylePickerWindow();
            WindowStylePickerVM model = new WindowStylePickerVM(catalog, initialStyle);
            viewModel = model;
            window.DataContext = model;
            window.StyleList.PreviewMouseLeftButtonUp += (_, e) =>
            {
                DependencyObject? element = e.OriginalSource as DependencyObject;
                while (element != null && element is not System.Windows.Controls.ListBoxItem)
                    element = VisualTreeHelper.GetParent(element);
                if (element is System.Windows.Controls.ListBoxItem item &&
                    item.DataContext is WindowStyleModel style &&
                    !string.IsNullOrEmpty(style.AssetPath))
                {
                    model.SelectedStyle = style;
                    window.DialogResult = true;
                }
            };
            window.PreviousButton.Click += (_, _) => model.PreviousPage();
            window.NextButton.Click += (_, _) => model.NextPage();
            window.CancelButton.Click += (_, _) => window.DialogResult = false;
            window.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    window.DialogResult = false;
                }
            };
            return window;
        }

        private static WindowStyleSelection? ShowParameters(
            WindowStyleModel style,
            WindowStyleSelection? initialSelection,
            double defaultWallThickness)
        {
            WindowStyleSelection result = new WindowStyleSelection
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
                MirrorAcrossWall = initialSelection?.MirrorAcrossWall == true
            };

            Document? document = Application.DocumentManager.MdiActiveDocument;
            Editor? editor = document?.Editor;
            while (true)
            {
                ArrangeWindowAction.WindowOpeningSizeResult? changed = new ArrangeWindowAction().ShowOpeningSize(
                    result,
                    style.DefaultWidth,
                    style.DefaultHeight,
                    result.WallThickness);
                if (changed == null) return null;

                result.Width = changed.Width;
                result.Height = changed.Height;
                result.WallThickness = changed.WallThickness;
                result.UseEdgeDistance = changed.UseEdgeDistance;
                result.EdgeDistance = changed.EdgeDistance;
                result.PlaceAtWallCenter = changed.PlaceAtWallCenter;
                result.ReverseAlongWall = changed.ReverseAlongWall;
                result.MirrorAcrossWall = changed.MirrorAcrossWall;

                if (changed.PickExistingWindowWidthRequested)
                {
                    if (editor == null || document == null) continue;

                    PromptEntityOptions options = new PromptEntityOptions("\nChọn một cửa sổ của VinaCAD tạo sẵn để lấy độ rộng: ");
                    options.SetRejectMessage("\nĐối tượng được chọn phải là block cửa sổ.");
                    options.AddAllowedClass(typeof(BlockReference), true);
                    PromptEntityResult pickedWindow = editor.GetEntity(options);
                    if (pickedWindow.Status == PromptStatus.OK)
                    {
                        if (WindowOpeningHelper.TryGetWindowWidth(document.Database, pickedWindow.ObjectId, out double width))
                            result.Width = Math.Round(width, 2);
                        else
                            editor.WriteMessage("\nBlock được chọn không phải cửa sổ do lệnh AW của VinaCAD tạo.");
                    }
                    continue;
                }

                if (changed.MeasureHoleRequested || changed.MeasurePierRequested)
                {
                    if (editor == null) continue;

                    string prompt = changed.MeasureHoleRequested ? "\nChọn hai điểm đo độ rộng cửa sổ: " : "\nChọn hai điểm đo khoảng cách trụ: ";
                    PromptDoubleResult measured = editor.GetDistance(new PromptDistanceOptions(prompt));
                    if (measured.Status == PromptStatus.OK && measured.Value > 0)
                    {
                        double value = Math.Round(measured.Value, 2);
                        if (changed.MeasureHoleRequested)
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

            return result;
        }
    }
}
