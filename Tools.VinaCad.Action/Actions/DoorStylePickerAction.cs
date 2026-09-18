using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.View.UI;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class DoorStylePickerAction
    {
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
                DoorOpeningSizeWindow window = new DoorOpeningSizeWindow(
                    result,
                    style.DefaultWidth,
                    style.DefaultHeight,
                    result.WallThickness);
                Application.ShowModalWindow(window);
                if (window.DialogResult != true) return null;

                result.Width = window.HoleWidth;
                result.Height = window.DoorHeight;
                result.WallThickness = window.WallThickness;
                result.UseEdgeDistance = window.IsPocketWidth && !window.PlaceAtWallCenter;
                result.EdgeDistance = window.PierWidth;
                result.PlaceAtWallCenter = window.PlaceAtWallCenter;
                result.ReverseAlongWall = window.ReverseAlongWall;
                result.MirrorAcrossWall = window.MirrorAcrossWall;

                if (window.PickExistingDoorWidthRequested)
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

                if (window.MeasureHoleRequested || window.MeasurePierRequested)
                {
                    if (editor == null) continue;

                    string prompt = window.MeasureHoleRequested? "\nChọn hai điểm đo độ rộng cửa: ": "\nChọn hai điểm đo khoảng cách trụ: ";
                    PromptDoubleResult measured = editor.GetDistance(new PromptDistanceOptions(prompt));
                    if (measured.Status == PromptStatus.OK && measured.Value > 0)
                    {
                        double value = Math.Round(measured.Value, 2);
                        if (window.MeasureHoleRequested)
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
    }
}
