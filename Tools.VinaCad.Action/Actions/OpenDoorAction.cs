using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class OpenDoorAction
    {
        public void Execute()
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            DoorStylePickerAction picker = new DoorStylePickerAction();
            DoorStyleSelection? selection = picker.Execute();
            if (selection == null) return;

            Editor editor = document.Editor;
            int createdCount = 0;
            while (true)
            {
                PromptEntityOptions options = CreateWallPrompt(selection);
                PromptEntityResult result = editor.GetEntity(options);
                if (result.Status == PromptStatus.Keyword)
                {
                    HandleKeyword(editor, picker, result.StringResult, ref selection);
                    continue;
                }
                if (result.Status != PromptStatus.OK) break;

                try
                {
                    OpenDoorHelper.CreateOpening(document.Database, result.ObjectId, result.PickedPoint, selection);
                    createdCount++;
                    editor.UpdateScreen();
                }
                catch (Exception exception)
                {
                    editor.WriteMessage($"\nAD: {exception.Message} Hãy chọn lại.");
                }
            }

            editor.WriteMessage($"\nAD: đã tạo {createdCount} cửa đi.");
        }

        private static PromptEntityOptions CreateWallPrompt(DoorStyleSelection selection)
        {
            string placement = selection.PlaceAtWallCenter ? "Giữa" : $"Pier={selection.EdgeDistance:0.##}";

            string orientation = (selection.ReverseAlongWall ? ", Đảo" : string.Empty) +
                                 (selection.MirrorAcrossWall ? ", Lật" : string.Empty);
            PromptEntityOptions options = new PromptEntityOptions(
                $"\nAD [{selection.Style.DisplayName}, W={selection.Width:0.##}, H={selection.Height:0.##}, " +
                $"T={selection.WallThickness:0.##}, {placement}{orientation}] " +
                "- Chọn tường hoặc [Kiểu/Thông số/Rộng/Cao/Dày/Trụ/Giữa/Đảo/Lật] <Enter: Kết thúc>: ")
            {
                AllowNone = true
            };
            options.SetRejectMessage("\nChỉ chấp nhận Line, Polyline thẳng hoặc block tường.");
            options.AddAllowedClass(typeof(Line), true);
            options.AddAllowedClass(typeof(Polyline), true);
            options.AddAllowedClass(typeof(BlockReference), true);
            options.Keywords.Add("Style", "Kieu", "Kiểu");
            options.Keywords.Add("Parameters", "Thongso", "Thôngsố");
            options.Keywords.Add("Width", "Rong", "Rộng");
            options.Keywords.Add("Height", "Cao", "Cao");
            options.Keywords.Add("Thickness", "Day", "Dày");
            options.Keywords.Add("Pier", "Tru", "Trụ");
            options.Keywords.Add("Center", "Giua", "Giữa");
            options.Keywords.Add("Reverse", "Dao", "Đảo");
            options.Keywords.Add("Flip", "Lat", "Lật");
            return options;
        }

        private static void HandleKeyword(
            Editor editor,
            DoorStylePickerAction picker,
            string keyword,
            ref DoorStyleSelection selection)
        {
            switch (keyword.ToUpperInvariant())
            {
                case "STYLE":
                case "KIEU":
                case "KIỂU":
                    DoorStyleSelection? changedStyle = picker.Execute(selection);
                    if (changedStyle != null) selection = changedStyle;
                    break;
                case "PARAMETERS":
                case "THONGSO":
                case "THÔNGSỐ":
                    DoorStyleSelection? changedParameters = picker.EditParameters(selection);
                    if (changedParameters != null) selection = changedParameters;
                    break;
                case "WIDTH":
                case "RONG":
                case "RỘNG":
                    if (TryPromptNumber(editor, "\nChiều rộng lỗ cửa W", selection.Width, false, out double width))
                        selection.Width = width;
                    break;
                case "HEIGHT":
                case "CAO":
                    if (TryPromptNumber(editor, "\nChiều cao cửa H", selection.Height, false, out double height))
                        selection.Height = height;
                    break;
                case "THICKNESS":
                case "DAY":
                case "DÀY":
                    if (TryPromptNumber(editor, "\nChiều dày tường dự kiến T", selection.WallThickness, false, out double thickness))
                        selection.WallThickness = thickness;
                    break;
                case "PIER":
                case "TRU":
                case "TRỤ":
                    if (TryPromptNumber(editor, "\nKhoảng trụ tường Pier", selection.EdgeDistance, true, out double pier))
                    {
                        selection.EdgeDistance = pier;
                        selection.PlaceAtWallCenter = false;
                        selection.UseEdgeDistance = true;
                    }
                    break;
                case "CENTER":
                case "GIUA":
                case "GIỮA":
                    selection.PlaceAtWallCenter = !selection.PlaceAtWallCenter;
                    selection.UseEdgeDistance = !selection.PlaceAtWallCenter;
                    if (selection.UseEdgeDistance && selection.EdgeDistance <= 0) selection.EdgeDistance = 200;
                    break;
                case "REVERSE":
                case "DAO":
                case "ĐẢO":
                    selection.ReverseAlongWall = !selection.ReverseAlongWall;
                    break;
                case "FLIP":
                case "LAT":
                case "LẬT":
                    selection.MirrorAcrossWall = !selection.MirrorAcrossWall;
                    break;
            }
        }

        private static bool TryPromptNumber(
            Editor editor,
            string label,
            double currentValue,
            bool allowZero,
            out double value)
        {
            PromptDoubleOptions options = new PromptDoubleOptions($"{label} <{currentValue:0.##}>: ")
            {
                AllowNegative = false,
                AllowZero = allowZero,
                DefaultValue = currentValue,
                UseDefaultValue = true
            };
            PromptDoubleResult result = editor.GetDouble(options);
            value = result.Status == PromptStatus.OK ? result.Value : currentValue;
            return result.Status == PromptStatus.OK;
        }
    }
}
