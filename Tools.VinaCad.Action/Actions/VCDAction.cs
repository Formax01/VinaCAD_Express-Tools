using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
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

            PromptPointResult ppr = ed.GetPoint(new PromptPointOptions("\nChọn điểm bắt đầu: "));
            if (ppr.Status != PromptStatus.OK)
                return;

            PromptDoubleResult lenRes = ed.GetDouble(
                new PromptDoubleOptions("\nNhập Length: ") { AllowNegative = false, AllowZero = false });
            if (lenRes.Status != PromptStatus.OK)
                return;

            PromptDoubleResult widRes = ed.GetDouble(
                new PromptDoubleOptions("\nNhập Width: ") { AllowNegative = false, AllowZero = false });
            if (widRes.Status != PromptStatus.OK)
                return;

            if (lenRes.Value < widRes.Value)
            {
                ed.WriteMessage("\nLength phải lớn hơn hoặc bằng Width.");
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
            ed.WriteMessage($"\nĐã vẽ 1 hình chữ nhật và {count} block.");
        }
    }
}