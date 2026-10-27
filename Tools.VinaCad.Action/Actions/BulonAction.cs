using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using Tools.Resources.Language;
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
            // PromptPointResult pRes = ed.GetPoint("\nChọn điểm: ");
            PromptPointResult pRes = ed.GetPoint(Lang.BULON_UI_ChonDiem);
            if (pRes.Status != PromptStatus.OK) return;

            // PromptDistanceOptions dOpt = new PromptDistanceOptions("\nNhập bán kính Bulon: ")
            PromptDistanceOptions dOpt = new PromptDistanceOptions(Lang.BULON_UI_NhapBanKinhBulon)
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
                // ed.WriteMessage($"\nĐã chèn block {BulonHelper.BLOCK_NAME}.");
                ed.WriteMessage(string.Format(Lang.BULON_MessDialog_DaChenBlock, BulonHelper.BLOCK_NAME));
                ed.Regen();
            }
        }
    }
}
