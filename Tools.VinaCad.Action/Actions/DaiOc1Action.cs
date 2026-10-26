using Teigha.DatabaseServices;
using Teigha.Geometry;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;

using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DaiOc1Action
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            // Chọn tâm
            PromptPointResult resP = ed.GetPoint(
                new PromptPointOptions("\nChọn điểm đặt: "));
            if (resP.Status != PromptStatus.OK) return;

            Point3d center = resP.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            // Nhập L
            PromptDoubleOptions optL = new PromptDoubleOptions("\nNhập L: ")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = 500,
                UseDefaultValue = true
            };
            PromptDoubleResult resL = ed.GetDouble(optL);
            if (resL.Status != PromptStatus.OK) return;

            

            // Ướm bằng jig và đặt
            if (!DaiOc1Helper.PlaceWithJig(doc, resL.Value, center))
                return;

            ed.Regen();
        }
    }
}
