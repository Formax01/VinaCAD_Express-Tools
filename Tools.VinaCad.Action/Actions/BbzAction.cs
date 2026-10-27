using System;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    
    public class BbzAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            BbzModel model = new BbzModel();

            // PromptPointResult pr = ed.GetPoint("\nChọn điểm trên đầu ống gió 1: ");
            PromptPointResult pr = ed.GetPoint(Lang.BBZ_UI_ChonDiemDauOng1);
            if (pr.Status != PromptStatus.OK)
                return;

            model.StartPoint = pr.Value;

            double w1;
            if (!TryGetDistance(ed, Lang.BBZ_UI_NhapBeRongOng1, model.StartPoint, null, out w1))
                return;
            model.Width1 = w1;

            double w2;
            if (!TryGetDistance(ed, Lang.BBZ_UI_NhapBeRongOng2, model.StartPoint, w1, out w2))
                return;
            model.Width2 = w2;

            PromptKeywordOptions side = new PromptKeywordOptions(Lang.BBZ_UI_ChonPhiaLuon, "Left Right")
            {
                AllowNone = true
            };
            side.Keywords.Default = "Left";

            PromptResult sideResult = ed.GetKeywords(side);
            if (sideResult.Status != PromptStatus.OK && sideResult.Status != PromptStatus.None)
                return;

            model.Side = sideResult.StringResult == "Right" ? BbzSide.Right : BbzSide.Left;

            double offset;
            if (!TryGetDistance(ed, Lang.BBZ_UI_NhapCaoDoLuon, model.StartPoint, null, out offset))
                return;
            model.Offset = offset;

            double length;
            if (!TryGetDistance(ed, Lang.BBZ_UI_NhapChieuDaiL, model.StartPoint, null, out length))
                return;
            model.Length = length;

            double height;
            if (!TryGetDistance(ed, Lang.BBZ_UI_NhapChieuCaoOngGio, model.StartPoint, null, out height))
                return;
            model.Height = height;

            string error;
            if (!BbzHelper.Validate(model, out error))
            {
                ed.WriteMessage(error);
                return;
            }

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            BbzPlacementJig jig = new BbzPlacementJig(model, ucs);
            PromptResult dragResult = ed.Drag(jig);

            if (dragResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage(Lang.BBZ_MessDialog_DaHuy);
                return;
            }

            Matrix3d xf = BbzHelper.BuildTransform(ucs, model.StartPoint, jig.Angle);

            Solid3d solid = BbzHelper.CreateDuctSolid(model, xf);
            BbzHelper.AppendToCurrentSpace(db, solid);

            ed.Regen();
            ed.WriteMessage(Lang.BBZ_MessDialog_DaVeOngGioChuZ3D);
        }

        private static bool TryGetDistance(
            Editor ed, string message, Point3d basePoint, double? defaultValue, out double value)
        {
            value = 0;

            PromptDistanceOptions opt = new PromptDistanceOptions(message)
            {
                AllowNegative = false,
                AllowZero = false,
                UseBasePoint = true,
                BasePoint = basePoint
            };

            if (defaultValue.HasValue)
            {
                opt.AllowNone = true;
                opt.DefaultValue = defaultValue.Value;
                opt.UseDefaultValue = true;
            }

            PromptDoubleResult r = ed.GetDistance(opt);

            if (r.Status == PromptStatus.None && defaultValue.HasValue)
            {
                value = defaultValue.Value;
                return true;
            }

            if (r.Status != PromptStatus.OK)
                return false;

            value = r.Value;
            return true;
        }
    }
}