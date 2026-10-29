using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Tools.VinaCAD.UI;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using PrLogTrackingSystem;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCAD.Action.Actions
{
    public class DrawWallAction
    {
        private DrawWallModel _wallModel;
        private Document? _document;
        private Editor? _editor;
        private Database? _database;

        public DrawWallAction()
        {
            _wallModel = new DrawWallModel();
            _document = null;
            _editor = null;
            _database = null;
        }

        public void Execute()
        {
            try
            {
                _document = Application.DocumentManager.MdiActiveDocument;
                _editor = _document?.Editor;
                _database = _document?.Database;

                if (_database == null || _editor == null)
                {
                    throw new Exception(Lang.WW_Error_KhongCoTaiLieuHoatDong);
                }

                SelectWallLayer(showCreatedMessage: true);

                _editor.WriteMessage("\n" + Lang.WW_Output_WwDVeTuongTChieuDayACanLeQThoat);

                while (true)
                {
                    PromptStringOptions pso = new PromptStringOptions("\n" + Lang.WW_Prompt_ChonDVeTuongTChieuDayACanLeQThoat)
                    {
                        AllowSpaces = false,
                        DefaultValue = "D"
                    };

                    PromptResult result = _editor.GetString(pso);

                    if (result.Status != PromptStatus.OK)
                        break;

                    string choice = result.StringResult.ToUpper();

                    switch (choice)
                    {
                        case "T":
                            if (!ChangeThickness())
                                return;
                            break;
                        case "A":
                            ChangeAlignment();
                            break;
                        case "D":
                            DrawWalls();
                            break;
                        case "Q":
                            return;
                        default:
                            _editor.WriteMessage("\n" + Lang.WW_Output_ChonDVeTChieuDayACanLeHoacQThoat);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.WW_Msg_Loi.Replace("{ex.Message}", ex.Message), Lang.Common_Text_Loi);
                Logger.Info(nameof(DrawWallAction), ex);
            }
        }

        private bool ChangeThickness()
        {
            if (_editor == null)
                return false;

            bool showDialog = true;
            while (showDialog)
            {
                var thicknessWindow = new Tools.VinaCAD.UI.WallThicknessWindow(_wallModel.Thickness);

                Prima.VinaCAD.ApplicationServices.Application.ShowModalWindow(thicknessWindow);

                if (thicknessWindow.DialogResult == true)
                {
                    if (thicknessWindow.IsPickUpRequested)
                    {
                        PromptDistanceOptions pdo = new PromptDistanceOptions("\n" + Lang.WW_Prompt_ChonHaiDiemXacDinhChieuDay);
                        PromptDoubleResult distRes = _editor.GetDistance(pdo);

                        if (distRes.Status == PromptStatus.OK && distRes.Value > 0)
                        {
                            _wallModel.Thickness = Math.Round(distRes.Value, 2);
                        }
                        showDialog = true; // Hiện lại bảng để người dùng confirm sau khi pick
                    }
                    else
                    {
                        _wallModel.Thickness = thicknessWindow.SelectedThickness;
                        showDialog = false;
                    }
                }
                else
                {
                    return false;
                }
            }

            _editor.WriteMessage("\n" + Lang.WW_Output_DaCapNhatChieuDayTuong.Replace("{_wallModel.Thickness}", _wallModel.Thickness.ToString()));
            return true;
        }

        private void ChangeAlignment()
        {
            if (_editor == null)
                return;

            PromptStringOptions psoAlign = new PromptStringOptions("\n" + Lang.WW_Prompt_CanTuong1Tam2Trai3Phai1)
            {
                AllowSpaces = false,
                DefaultValue = "1"
            };

            PromptResult alignResult = _editor.GetString(psoAlign);
            if (alignResult.Status == PromptStatus.OK)
            {
                switch (alignResult.StringResult)
                {
                    case "1":
                        _wallModel.Alignment = WallAlignment.Center;
                        _editor.WriteMessage("\n" + Lang.WW_Output_DaChonCanTam);
                        break;
                    case "2":
                        _wallModel.Alignment = WallAlignment.Left;
                        _editor.WriteMessage("\n" + Lang.WW_Output_DaChonCanTrai);
                        break;
                    case "3":
                        _wallModel.Alignment = WallAlignment.Right;
                        _editor.WriteMessage("\n" + Lang.WW_Output_DaChonCanPhai);
                        break;
                    default:
                        _editor.WriteMessage("\n" + Lang.WW_Output_LuaChonKhongHopLeGiuNguyenCanLeHienTai);
                        break;
                }
            }
        }

        private void DrawWalls()
        {
            if (_editor == null)
                return;

            SelectWallLayer(showCreatedMessage: false);

            _editor.WriteMessage("\n" + Lang.WW_Output_ChonCacDiemLienTiepNhanEnterHoacEscDeKetThuc);

            List<Point3d> wallPoints = new List<Point3d>();

            while (true)
            {
                string prompt = wallPoints.Count == 0
                    ? "\n" + Lang.WW_Prompt_DiemDau
                    : "\n" + Lang.WW_Prompt_DiemTiepTheoKetThuc;
                PromptPointOptions ppo = new PromptPointOptions(prompt)
                {
                    AllowArbitraryInput = false
                };

                if (wallPoints.Count > 0)
                {
                    ppo.UseBasePoint = true;
                    ppo.BasePoint = wallPoints[wallPoints.Count - 1];
                }

                PromptPointResult pResult = _editor.GetPoint(ppo);

                if (pResult.Status == PromptStatus.Cancel || pResult.Status == PromptStatus.None)
                {
                    break;
                }

                if (pResult.Status == PromptStatus.OK)
                {
                    Point3d newPoint = pResult.Value;

                    // Add point to list
                    if (wallPoints.Count == 0 || !newPoint.IsEqualTo(wallPoints[wallPoints.Count - 1]))
                    {
                        DrawWallHelper.RemoveCapAt(_database, newPoint, _wallModel.Thickness, _wallModel.WallLayer);

                        wallPoints.Add(newPoint);

                        // If we have at least 2 points, draw the wall segment
                        if (wallPoints.Count >= 2)
                        {
                            DrawWallSegment(wallPoints[wallPoints.Count - 2], wallPoints[wallPoints.Count - 1]);
                        }
                    }
                }
            }

            if (wallPoints.Count > 1)
            {
                try
                {
                    Point3d firstPoint = wallPoints[0];
                    Point3d lastPoint = wallPoints[wallPoints.Count - 1];

                    DrawWallHelper.CapFreeEnd(_database, firstPoint, _wallModel.Thickness, _wallModel.WallLayer);

                    if (!lastPoint.IsEqualTo(firstPoint))
                    {
                        DrawWallHelper.CapFreeEnd(_database, lastPoint, _wallModel.Thickness, _wallModel.WallLayer);
                    }

                    _editor.UpdateScreen();
                }
                catch (Exception ex)
                {
                    _editor.WriteMessage("\n" + Lang.WW_Output_LoiBoDauTuong.Replace("{ex.Message}", ex.Message));
                    Logger.Info(nameof(DrawWalls), ex);
                }

                _editor.WriteMessage("\n" + Lang.WW_Output_DaVeDoanTuong.Replace("{wallPoints.Count - 1}", (wallPoints.Count - 1).ToString()));
            }
        }

        private void SelectWallLayer(bool showCreatedMessage)
        {
            if (_database == null) return;

            _wallModel.WallLayer = DrawWallHelper.EnsureWallLayer(
                _database, "Wall", out bool wasCreated);

            if (showCreatedMessage && wasCreated)
                _editor?.WriteMessage("\n" + Lang.WW_Output_DaTaoVaChonLayerWall);
        }

        private void DrawWallSegment(Point3d startPoint, Point3d endPoint)
        {
            if (_database == null || _editor == null)
                return;

            List<ObjectId>? lineIds = null;

            try
            {
                // Calculate wall lines
                DrawWallHelper.CalculateWallLines(
                    startPoint,
                    endPoint,
                    _wallModel.Thickness,
                    _wallModel.Alignment,
                    out Point3d line1Start,
                    out Point3d line1End,
                    out Point3d line2Start,
                    out Point3d line2End);

                // Find existing intersections
                var intersections = DrawWallHelper.FindWallIntersections(
                    _database,
                    line1Start,
                    line1End,
                    line2Start,
                    line2End,
                    _wallModel.WallLayer);

                // Create wall lines
                lineIds = DrawWallHelper.CreateWallLines(
                    _database,
                    line1Start,
                    line1End,
                    line2Start,
                    line2End,
                    _wallModel.WallLayer);

                // Clean up intersections
                if (intersections.Count > 0)
                {
                    DrawWallHelper.CleanupIntersections(
                    _database,
                    lineIds,
                    intersections,
                    _wallModel.WallLayer,
                    _wallModel.Thickness,
                    _wallModel.Alignment);
                }

                _editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                if (lineIds != null && lineIds.Count > 0)
                {
                    try
                    {
                        DrawWallHelper.EraseEntities(_database, lineIds);
                    }
                    catch (Exception rollbackEx)
                    {
                        Logger.Info($"{nameof(DrawWallSegment)}_Rollback", rollbackEx);
                    }
                }

                _editor.WriteMessage("\n" + Lang.WW_Output_LoiVeTuong.Replace("{ex.Message}", ex.Message));
                Logger.Info(nameof(DrawWallSegment), ex);
            }
        }
    }
}
