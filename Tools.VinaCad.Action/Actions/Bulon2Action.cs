using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class Bulon2Action
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            // Điểm đặt (giữa đáy)
            PromptPointResult resP = ed.GetPoint(
                new PromptPointOptions("\nChọn điểm đặt: "));
            if (resP.Status != PromptStatus.OK) return;

            Point3d origin = resP.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            // B2: nhập L
            PromptDoubleOptions optL = new PromptDoubleOptions("\nNhập L: ")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = 500,
                UseDefaultValue = true
            };
            PromptDoubleResult resL = ed.GetDouble(optL);
            if (resL.Status != PromptStatus.OK) return;

            

            // B3: vẽ (có jig ướm)
            if (!Bulon2Helper.PlaceWithJig(doc, resL.Value, origin))
                return;

            ed.Regen();
        }
    }
}
