using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class ArrangeWindowAction
    {
        public void Execute()
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

                /*try
                {
                    PromptStatus directionStatus = WindowOpeningHelper.JigWindowDirection(
                        editor,
                        document.Database,
                        windowId,
                        selection);
                    editor.UpdateScreen();
                    if (directionStatus == PromptStatus.Cancel) break;
                }
                catch (Exception exception)
                {
                    editor.WriteMessage($"\nAW: Không thể đổi hướng cửa sổ: {exception.Message}");
                }*/
            }

            editor.WriteMessage($"\nAW: đã tạo {createdCount} cửa sổ.");
        }

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
