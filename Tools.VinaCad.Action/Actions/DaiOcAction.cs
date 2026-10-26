using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;
namespace Tools.VinaCad.Action.Actions
{
    public class DaiOcAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            // Chọn tâm
            PromptPointResult pRes = ed.GetPoint("\nChọn điểm đặt: ");
            if (pRes.Status != PromptStatus.OK) return;

            // Nhập L (bán kính)
            PromptDistanceOptions dOpt = new PromptDistanceOptions("\nNhập L : ")
            {
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult dRes = ed.GetDistance(dOpt);
            if (dRes.Status != PromptStatus.OK) return;
            double l = dRes.Value;

           

            // Vẽ (Jig ướm hình) - có thêm vòng tròn bán kính L/2
            int count = BulonHelper.CreateBolt(
                doc.Database, ed, pRes.Value, l,
                BulonHelper.DAIOC_BLOCK_NAME, true);

            if (count > 0)
            {
                ed.WriteMessage($"\nĐã chèn block {BulonHelper.DAIOC_BLOCK_NAME}.");
                ed.Regen();
            }
        }
    }
}