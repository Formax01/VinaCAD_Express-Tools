using Teigha.DatabaseServices;
using Teigha.Geometry;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;

using Tools.Resources.Language;
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

        // PromptPointOptions optP = new PromptPointOptions("\nChọn điểm đặt: ");
        PromptPointOptions optP = new PromptPointOptions(Lang.BULON1_UI_ChonDiemDat);
        PromptPointResult resP = ed.GetPoint(optP);
        if (resP.Status != PromptStatus.OK) return;

        // PromptDoubleOptions optL = new PromptDoubleOptions("\nNhập L: ")
        PromptDoubleOptions optL = new PromptDoubleOptions(Lang.BULON1_UI_NhapL)
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
