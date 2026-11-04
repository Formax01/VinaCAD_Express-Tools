using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.Resources.Language;
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
                        editor.WriteMessage("\n" + Lang.AD_Output_AdHayChonLai.Replace("{exception.Message}", exception.Message));
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
                        editor.WriteMessage("\n" + Lang.AD_Output_AdKhongTheDoiHuongCua.Replace("{exception.Message}", exception.Message));
                    }
                }

                editor.WriteMessage("\n" + Lang.AD_Output_AdDaTaoCuaDi.Replace("{createdCount}", createdCount.ToString()));
            }
            catch (Exception exception)
            {
                Logger.Info("CreateDoorCommand", exception);

                System.Windows.MessageBox.Show(
                    Lang.AD_Msg_LoiAd.Replace("{exception.Message}", exception.Message),
                    Lang.Common_Text_Loi,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static PromptEntityOptions CreateWallPrompt(DoorStyleSelection selection)
        {
            PromptEntityOptions options = new PromptEntityOptions(Lang.AD_Prompt_ChonTuongHoacSChonCuaWCaiDatThongSoC)
            {
                AllowNone = true
            };
            options.SetRejectMessage("\n" + Lang.Common_Prompt_ChiChapNhanLinePolylineThangHoacBlockTuong);
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
                    editor.WriteMessage("\n " + Lang.AD_Output_DaChuyenViTriCuaVeGiuaTuong);
                    return true;
            }

            return true;
        }
    }
}
