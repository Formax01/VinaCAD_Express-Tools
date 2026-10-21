using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
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

            PromptPointResult ptRes = ed.GetPoint("\nChọn điểm bắt đầu: ");
            if (ptRes.Status != PromptStatus.OK) return;

            if (!TryGetSize(ed, "\nLength: ", out double length)) return;
            if (!TryGetSize(ed, "\nWidth: ", out double width)) return;
            if (!TryGetSize(ed, "\nHeight: ", out double height)) return;

            // Jig: ướm hình, xoay quanh điểm bắt đầu
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            DrawBoxJig jig = new DrawBoxJig(ucs, ptRes.Value, length, width, height);
            PromptResult jigRes = ed.Drag(jig);

            if (jigRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nĐã hủy.");
                return;
            }

            DrawBoxHelper.CreateBox(
                doc.Database, ucs, ptRes.Value, length, width, height, jig.Angle);

            ed.Regen();
            ed.WriteMessage($"\nĐã vẽ ống gió :{length} x {width} x {height}.");
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