using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class OpenDoorAction
    {
        Editor? editor = null;
        public void Execute()
        {
            try
            {
                Document? document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;

                DoorStylePickerAction picker = new DoorStylePickerAction();
                DoorStyleSelection selection = picker.CreateDefaultSelection();

                Editor editor = document.Editor;
                int createdCount = 0;
                while (true)
                {
                    PromptEntityOptions options = CreateWallPrompt(selection);
                    PromptEntityResult result = editor.GetEntity(options);
                    if (result.Status == PromptStatus.Keyword)
                    {
                        if (!HandleActiveKeyword(editor, picker, result.StringResult, ref selection))
                            return;
                        continue;
                    }
                    if (result.Status != PromptStatus.OK) break;

                    ObjectId doorId;
                    try
                    {
                        doorId = OpenDoorHelper.CreateOpening(
                            document.Database,
                            result.ObjectId,
                            result.PickedPoint,
                            selection);
                        editor.UpdateScreen();
                    }
                    catch (Exception exception)
                    {
                        editor.WriteMessage($"\nAD: {exception.Message} Hãy chọn lại.");
                        continue;
                    }

                    try
                    {
                        PromptStatus directionStatus = OpenDoorHelper.JigDoorDirection(
                            editor,
                            document.Database,
                            doorId,
                            selection);
                        editor.UpdateScreen();
                        if (directionStatus != PromptStatus.OK)
                        {
                            OpenDoorHelper.CancelOpening(document.Database, doorId);
                            editor.UpdateScreen();
                            break;
                        }

                        createdCount++;
                        OpenDoorHelper.CommitOpening(doorId);
                    }
                    catch (Exception exception)
                    {
                        OpenDoorHelper.CancelOpening(document.Database, doorId);
                        editor.WriteMessage($"\nAD: Không thể đổi hướng cửa: {exception.Message}");
                    }
                }

                editor.WriteMessage($"\nAD: đã tạo {createdCount} cửa đi.");
            }
            catch (Exception exception)
            {
                Logger.Info("CreateDoorCommand", exception);

                System.Windows.MessageBox.Show($"Lỗi AD: {exception.Message}",StringDefinition.TITLE_ERROR,MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }

        private static PromptEntityOptions CreateWallPrompt(DoorStyleSelection selection)
        {
            string placement = selection.PlaceAtWallCenter ? "Giữa" : $"Pier={selection.EdgeDistance:0.##}";

            string orientation = (selection.ReverseAlongWall ? ", Đảo" : string.Empty) +
                                 (selection.MirrorAcrossWall ? ", Lật" : string.Empty);
            PromptEntityOptions options = new PromptEntityOptions("Chọn tường hoặc [S-Chọn cửa/W-Cài đặt thông số/C-Center] <Enter: Kết thúc>: ")
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

        private static bool HandleActiveKeyword(
            Editor editor,
            DoorStylePickerAction picker,
            string keyword,
            ref DoorStyleSelection selection)
        {
            switch (keyword.ToUpperInvariant())
            {
                case "STYLE":
                case "A":
                    return picker.SelectStyle(selection);
                case "PARAMETERS":
                case "W":
                    DoorStyleSelection? changed = picker.EditParameters(selection);
                    if (changed == null) return false;
                    selection = changed;
                    return true;
                case "CENTER":
                case "C":
                    selection.PlaceAtWallCenter = true;
                    selection.UseEdgeDistance = false;
                    editor.WriteMessage("\n Đã chuyển vị trí cửa về giữa tường.");
                    return true;
            }

            return true;
        }
    }
}
