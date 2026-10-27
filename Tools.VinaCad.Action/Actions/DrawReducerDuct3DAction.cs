using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
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

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // B1: điểm đầu (tâm mặt đáy)
            // PromptPointResult pr = ed.GetPoint("\nChọn điểm đầu: ");
            PromptPointResult pr = ed.GetPoint(Lang.BB2_UI_ChonDiemDau);
            if (pr.Status != PromptStatus.OK) return;
            Matrix3d ucs2wcs = ed.CurrentUserCoordinateSystem;
            Point3d origin = pr.Value.TransformBy(ucs2wcs);

            // B2 -> B6
            if (!GetPositive(ed, Lang.BB2_UI_NhapLengthOX, ref _length)) return;
            if (!GetPositive(ed, Lang.BB2_UI_NhapWidthOY, ref _width)) return;
            if (!GetPositive(ed, Lang.BB2_UI_NhapHeightOZ, ref _height)) return;
            if (!GetPositive(ed, Lang.BB2_UI_NhapSecondWidthOY, ref _width2)) return;
            if (!GetPositive(ed, Lang.BB2_UI_NhapSecondHeightOZ, ref _height2)) return;

            // B7: vị trí Second Width
            if (!GetKeyword(ed, Lang.BB2_UI_ViTriSecondWidth, ref _yAlign,
                    new[] { Lang.BB2_UI_Giua, Lang.BB2_UI_Trai, Lang.BB2_UI_PHAI },
                    new[] { Lang.BB2_UI_Giua, Lang.BB2_UI_Trai, Lang.BB2_UI_PHAI })) return;

            // B8: vị trí Second Height
            if (!GetKeyword(ed, Lang.BB2_UI_ViTriSecondHeight, ref _zAlign,
                    new[] { Lang.BB2_UI_Tren, Lang.BB2_UI_Mid, Lang.BB2_UI_Duoi },
                    new[] { Lang.BB2_UI_Tren, Lang.BB2_UI_Mid, Lang.BB2_UI_Duoi })) return;

            ReducerDuct3DModel model = new ReducerDuct3DModel
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

            // B9 + B10: dựng hình, ướm xoay quanh điểm đầu
            Point3d[] local = ReducerDuct3DHelper.BuildLocalVertices(model);
            
            ReducerDuct3DJig jig = new ReducerDuct3DJig(local, origin, ucs2wcs);
            PromptResult dragResult = ed.Drag(jig);
            if (dragResult.Status != PromptStatus.OK) return;

            Point3d[] world = ReducerDuct3DHelper.ToWorld(local, origin, jig.Angle);
            ReducerDuct3DHelper.CreateMesh(db, world);

            ed.Regen();
            ed.WriteMessage(Lang.BB2_MessDialog_DaVeOngGioGiamCap3D);
        }

        private static bool GetPositive(Editor ed, string message, ref double value)
        {
            PromptDoubleOptions opt = new PromptDoubleOptions(message)
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = value,
                UseDefaultValue = true
            };
            PromptDoubleResult r = ed.GetDouble(opt);
            if (r.Status != PromptStatus.OK) return false;
            value = r.Value;
            return true;
        }

        private static bool GetKeyword(Editor ed, string message, ref string current,
                               string[] globalNames, string[] displayNames)
        {
            PromptKeywordOptions opt = new PromptKeywordOptions(message) { AllowNone = true };
            for (int i = 0; i < globalNames.Length; i++)
                opt.Keywords.Add(globalNames[i], displayNames[i], displayNames[i]);

            opt.Keywords.Default = Array.IndexOf(globalNames, current) >= 0
                ? current : globalNames[0];

            PromptResult r = ed.GetKeywords(opt);
            if (r.Status != PromptStatus.OK) return false;
            current = r.StringResult;   // luôn trả về globalName: Giua/Trai/Phai/Tren/Duoi
            return true;
        }

    }
}