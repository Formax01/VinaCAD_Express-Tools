using Teigha.DatabaseServices;
using Teigha.Geometry;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;

using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

public class DaiOc2Action
{
    public void Execute()
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        // PromptPointResult resP = ed.GetPoint(new PromptPointOptions("\nChọn điểm đặt: "));
        PromptPointResult resP = ed.GetPoint(
            new PromptPointOptions(Lang.DAIOC2_UI_ChonDiemDat));
        if (resP.Status != PromptStatus.OK) return;

        Point3d origin = resP.Value.TransformBy(ed.CurrentUserCoordinateSystem);

        // PromptDoubleOptions optL = new PromptDoubleOptions("\nNhập L: ")
        PromptDoubleOptions optL = new PromptDoubleOptions(Lang.DAIOC2_UI_NhapL)
        {
            AllowNegative = false,
            AllowZero = false,
            DefaultValue = 500,
            UseDefaultValue = true
        };
        PromptDoubleResult resL = ed.GetDouble(optL);
        if (resL.Status != PromptStatus.OK) return;

        // Ướm bằng jig và đặt
        if (!DaiOc2Helper.PlaceWithJig(doc, resL.Value, origin))
            return;

        ed.Regen();
    }
}
