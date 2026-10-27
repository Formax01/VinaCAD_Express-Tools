using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawBoxAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            // PromptPointResult ptRes = ed.GetPoint("\nChọn điểm bắt đầu: ");
            PromptPointResult ptRes = ed.GetPoint(Lang.BB1_UI_ChonDiemBatDau);
            if (ptRes.Status != PromptStatus.OK) return;

            if (!TryGetSize(ed, Lang.BB1_UI_NhapLength, out double length)) return;
            if (!TryGetSize(ed, Lang.BB1_UI_NhapWidth, out double width)) return;
            if (!TryGetSize(ed, Lang.BB1_UI_NhapHeight, out double height)) return;

            // Jig: ướm hình, xoay quanh điểm bắt đầu
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            DrawBoxJig jig = new DrawBoxJig(ucs, ptRes.Value, length, width, height);
            PromptResult jigRes = ed.Drag(jig);

            if (jigRes.Status != PromptStatus.OK)
            {
                // ed.WriteMessage("\nĐã hủy.");
                ed.WriteMessage(Lang.BB1_MessDialog_DaHuy);
                return;
            }

            DrawBoxHelper.CreateBox(
                doc.Database, ucs, ptRes.Value, length, width, height, jig.Angle);

            ed.Regen();
            // ed.WriteMessage($"\nĐã vẽ ống gió :{length} x {width} x {height}.");
            ed.WriteMessage(string.Format(Lang.BB1_MessDialog_DaVeOngGio, length, width, height));
        }

        private static bool TryGetSize(Editor ed, string message, out double value)
        {
            value = 0;

            PromptDoubleOptions opt = new PromptDoubleOptions(message)
            {
                AllowZero = false,
                AllowNegative = false,
                AllowNone = false
            };

            PromptDoubleResult res = ed.GetDouble(opt);
            if (res.Status != PromptStatus.OK) return false;

            value = res.Value;
            return true;
        }
    }
}