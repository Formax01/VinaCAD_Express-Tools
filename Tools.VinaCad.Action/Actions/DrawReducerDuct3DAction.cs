using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawReducerDuct3DAction
    {
        // Nhớ giá trị lần nhập trước để gõ Enter cho nhanh
        private static double _length = 500, _width = 400, _height = 300,
                              _width2 = 300, _height2 = 200;
        private static string _yAlign = Lang.BB2_UI_Giua;
        private static string _zAlign = Lang.BB2_UI_Mid;

        private Document _doc;
        private Editor _ed;
        private Database _db;
        private Matrix3d _ucs;

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    return;

                // B1: điểm đầu (tâm mặt đáy)
                PromptPointResult startRes = _ed.GetPoint(Lang.BB2_UI_ChonDiemDau);
                if (startRes.Status != PromptStatus.OK)
                    return;
                Point3d origin = startRes.Value.TransformBy(_ucs);

                // B2 -> B6: nhập kích thước
                if (!TryGetSizes())
                    return;

                // B7, B8: vị trí Second Width và Second Height
                if (!TryGetAlignments())
                    return;

                ReducerDuct3DModel model = BuildModel();

                // B9 + B10: dựng hình, ướm xoay quanh điểm đầu
                Point3d[] local = ReducerDuct3DHelper.BuildLocalVertices(model);

                ReducerDuct3DJig jig = new ReducerDuct3DJig(local, origin, _ucs);
                if (_ed.Drag(jig).Status != PromptStatus.OK)
                    return;

                Point3d[] world = ReducerDuct3DHelper.ToWorld(local, origin, jig.Angle);
                ReducerDuct3DHelper.CreateMesh(_db, world);

                Finish();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawReducerDuct3DAction), ex);
                throw;
            }
        }

        private void UpdateCurrentDocument()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            if (_doc == null)
                return;

            _ed = _doc.Editor;
            _db = _doc.Database;
            _ucs = _ed.CurrentUserCoordinateSystem;
        }

        // ---------- Các bước nhập ----------

        // B2 -> B6: năm kích thước, Enter = giá trị lần trước
        private bool TryGetSizes()
        {
            return GetPositive(Lang.BB2_UI_NhapLengthOX, ref _length)
                && GetPositive(Lang.BB2_UI_NhapWidthOY, ref _width)
                && GetPositive(Lang.BB2_UI_NhapHeightOZ, ref _height)
                && GetPositive(Lang.BB2_UI_NhapSecondWidthOY, ref _width2)
                && GetPositive(Lang.BB2_UI_NhapSecondHeightOZ, ref _height2);
        }

        // B7, B8: vị trí Second Width (Giữa/Trái/Phải) và Second Height (Trên/Giữa/Dưới)
        private bool TryGetAlignments()
        {
            string[] yNames = { Lang.BB2_UI_Giua, Lang.BB2_UI_Trai, Lang.BB2_UI_PHAI };
            string[] zNames = { Lang.BB2_UI_Tren, Lang.BB2_UI_Mid, Lang.BB2_UI_Duoi };

            return GetKeyword(Lang.BB2_UI_ViTriSecondWidth, ref _yAlign, yNames, yNames)
                && GetKeyword(Lang.BB2_UI_ViTriSecondHeight, ref _zAlign, zNames, zNames);
        }

        private bool GetPositive(string message, ref double value)
        {
            PromptDoubleOptions options = new PromptDoubleOptions(message)
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = value,
                UseDefaultValue = true
            };

            PromptDoubleResult result = _ed.GetDouble(options);
            if (result.Status != PromptStatus.OK)
                return false;

            value = result.Value;
            return true;
        }

        private bool GetKeyword(string message, ref string current,
                                string[] globalNames, string[] displayNames)
        {
            PromptKeywordOptions options = new PromptKeywordOptions(message) { AllowNone = true };
            for (int i = 0; i < globalNames.Length; i++)
                options.Keywords.Add(globalNames[i], displayNames[i], displayNames[i]);

            options.Keywords.Default = Array.IndexOf(globalNames, current) >= 0
                ? current
                : globalNames[0];

            PromptResult result = _ed.GetKeywords(options);
            if (result.Status != PromptStatus.OK)
                return false;

            current = result.StringResult;   // luôn trả về globalName: Giua/Trai/Phai/Tren/Duoi
            return true;
        }

        // ---------- Dựng model và kết thúc ----------

        private static ReducerDuct3DModel BuildModel()
        {
            return new ReducerDuct3DModel
            {
                Length = _length,
                Width = _width,
                Height = _height,
                Width2 = _width2,
                Height2 = _height2,
                YAlign = _yAlign == Lang.BB2_UI_Trai ? DuctYAlign.Left
                       : _yAlign == Lang.BB2_UI_PHAI ? DuctYAlign.Right
                       : DuctYAlign.Center,
                ZAlign = _zAlign == Lang.BB2_UI_Tren ? DuctZAlign.Top
                       : _zAlign == Lang.BB2_UI_Duoi ? DuctZAlign.Bottom
                       : DuctZAlign.Middle
            };
        }

        private void Finish()
        {
            _ed.Regen();
            _ed.WriteMessage(Lang.BB2_MessDialog_DaVeOngGioGiamCap3D);
        }
    }
}