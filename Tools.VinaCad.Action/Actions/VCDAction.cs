using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class VCDAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult ppr = ed.GetPoint(new PromptPointOptions("\n" + Lang.VCD_UI_ChonDiemBatDau));
            if (ppr.Status != PromptStatus.OK)
                return;

            PromptDoubleResult lenRes = ed.GetDouble(
                new PromptDoubleOptions("\n" + Lang.VCD_UI_NhapLength) { AllowNegative = false, AllowZero = false });
            if (lenRes.Status != PromptStatus.OK)
                return;

            PromptDoubleResult widRes = ed.GetDouble(
                new PromptDoubleOptions("\n" + Lang.VCD_UI_NhapWidth) { AllowNegative = false, AllowZero = false });
            if (widRes.Status != PromptStatus.OK)
                return;

            if (lenRes.Value < widRes.Value)
            {
                ed.WriteMessage("\n" + Lang.VCD_MessDialog_LengthPhaiLonHonHoacBangWidth);
                return;
            }

            // Jig ướm hình, xoay quanh góc dưới trái
            VCDJig jig = new VCDJig(
                db, ppr.Value, lenRes.Value, widRes.Value, ed.CurrentUserCoordinateSystem);

            PromptResult dragRes = ed.Drag(jig);
            if (dragRes.Status != PromptStatus.OK)
                return;

            int count = VCDHelper.CreateShape(
                db,
                ppr.Value,
                lenRes.Value,
                widRes.Value,
                jig.Rotation,
                ed.CurrentUserCoordinateSystem);

            ed.Regen();
            ed.WriteMessage("\n" + string.Format(Lang.VCD_MessDialog_DaVeHinhChuNhatVaBlock, count));
        }
    }
}
