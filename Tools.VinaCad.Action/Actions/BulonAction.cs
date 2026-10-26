using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application= Prima.VinaCAD.ApplicationServices.Application;
namespace Tools.VinaCad.Action.Actions
{
    public class BulonAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            // Chọn tâm bulon
            PromptPointResult pRes = ed.GetPoint("\nChọn điểm: ");
            if (pRes.Status != PromptStatus.OK) return;

            // B2: nhập L (bán kính bulon)
            PromptDistanceOptions dOpt = new PromptDistanceOptions("\nNhập bán kính Bulon: ")
            {
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult dRes = ed.GetDistance(dOpt);
            if (dRes.Status != PromptStatus.OK) return;
            double l = dRes.Value;

           

            // B3: vẽ
            int count = BulonHelper.CreateBolt(doc.Database, ed, pRes.Value, l);
            if (count > 0)
            {
                ed.WriteMessage($"\nĐã chèn block {BulonHelper.BLOCK_NAME}.");
                ed.Regen();
            }
        }
    }
}
