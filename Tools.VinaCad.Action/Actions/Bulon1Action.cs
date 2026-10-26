using Teigha.DatabaseServices;
using Teigha.Geometry;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;

using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

public class Bulon1Action
{
    public void Execute()
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        

        // Chọn điểm đặt (tâm đáy)
        PromptPointOptions optP = new PromptPointOptions("\nChọn điểm đặt: ");
        PromptPointResult resP = ed.GetPoint(optP);
        if (resP.Status != PromptStatus.OK) return;

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

        // B3: vẽ
        
        Point3d origin = resP.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        if (!Bulon1Helper.PlaceWithJig(doc, resL.Value, origin))
            return;
        ed.Regen();
    }
}