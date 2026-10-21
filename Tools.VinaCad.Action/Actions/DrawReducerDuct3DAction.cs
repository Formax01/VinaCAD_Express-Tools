using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawReducerDuct3DAction
    {
        // Nhớ giá trị lần nhập trước để gõ Enter cho nhanh
        private static double _length = 500, _width = 400, _height = 300,
                              _width2 = 300, _height2 = 200;
        private static string _yAlign = "Giua";
        private static string _zAlign = "Giua";

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // B1: điểm đầu (tâm mặt đáy)
            PromptPointResult pr = ed.GetPoint("\nChọn điểm đầu: ");
            if (pr.Status != PromptStatus.OK) return;
            Matrix3d ucs2wcs = ed.CurrentUserCoordinateSystem;
            Point3d origin = pr.Value.TransformBy(ucs2wcs);
         

            // B2 -> B6
            if (!GetPositive(ed, "\nLength (OX)", ref _length)) return;
            if (!GetPositive(ed, "\nWidth (OY)", ref _width)) return;
            if (!GetPositive(ed, "\nHeight (OZ)", ref _height)) return;
            if (!GetPositive(ed, "\nSecond Width (OY)", ref _width2)) return;
            if (!GetPositive(ed, "\nSecond Height (OZ)", ref _height2)) return;

            // B7: vị trí Second Width
            if (!GetKeyword(ed, "\nVị trí Second Width", ref _yAlign,
                    new[] { "Giua", "Trai", "Phai" },
                    new[] { "Giữa", "Trái", "Phải" })) return;

            // B8: vị trí Second Height
            if (!GetKeyword(ed, "\nVị trí Second Height", ref _zAlign,
                    new[] { "Tren", "Giua", "Duoi" },
                    new[] { "Trên", "Giữa", "Dưới" })) return;

            ReducerDuct3DModel model = new ReducerDuct3DModel
            {
                Length = _length,
                Width = _width,
                Height = _height,
                Width2 = _width2,
                Height2 = _height2,
                YAlign = _yAlign == "Trai" ? DuctYAlign.Left
                       : _yAlign == "Phai" ? DuctYAlign.Right
                       : DuctYAlign.Center,
                ZAlign = _zAlign == "Tren" ? DuctZAlign.Top
                       : _zAlign == "Duoi" ? DuctZAlign.Bottom
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
            ed.WriteMessage("\nĐã vẽ ống gió giảm cấp 3D.");
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