using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class HGAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            // B2: điểm đầu
            PromptPointResult p1 = ed.GetPoint(new PromptPointOptions("\n" + Lang.HG_UI_ChonDiemDau));
            if (p1.Status != PromptStatus.OK) return;

            Point3d startWcs = p1.Value.TransformBy(ucs);

            // B3: điểm cuối (jig ướm hình theo chiều dài)
            HGJig endJig = HGJig.ForEndPoint(p1.Value, ucs);
            PromptResult r2 = ed.Drag(endJig);
            if (r2.Status != PromptStatus.OK) return;

            Vector3d dir = endJig.EndWcs - startWcs;
            double length = dir.Length;
            if (length < HGHelper.LineLength)
            {
                ed.WriteMessage("\n" + string.Format(Lang.HG_MessDialog_KhoangCachPhaiLonHon, HGHelper.LineLength));
                return;
            }
            double axisAngle = Math.Atan2(dir.Y, dir.X);

            // B4: góc quay (jig ướm hình xoay quanh điểm đầu)
            HGJig angleJig = HGJig.ForAngle(p1.Value, ucs, length, axisAngle);
            PromptResult r3 = ed.Drag(angleJig);

            double angle;
            if (r3.Status == PromptStatus.OK)
                angle = angleJig.AngleWcs;
            else if (angleJig.NullResponse)
                angle = axisAngle;                 // Enter = theo hướng đường nối
            else
                return;                            // Esc

            // Tạo block 1 lần duy nhất, chèn tại điểm đầu
            ObjectId brId = HGHelper.CreateBlockReference(db, startWcs, length, angle);
            if (brId.IsNull) return;

            ed.Regen();
            ed.WriteMessage("\n" + Lang.HG_MessDialog_DaTaoBlock);
        }
    }
}
