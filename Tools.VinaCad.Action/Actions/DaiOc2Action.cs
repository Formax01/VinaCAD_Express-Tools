using Teigha.DatabaseServices;
using Teigha.Geometry;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;

using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

public class DaiOc2Action
{
    public void Execute()
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        // Chọn trung điểm cạnh đáy
        PromptPointResult resP = ed.GetPoint(
            new PromptPointOptions("\nChọn điểm đặt: "));
        if (resP.Status != PromptStatus.OK) return;

        Point3d origin = resP.Value.TransformBy(ed.CurrentUserCoordinateSystem);

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
        if (!DaiOc2Helper.PlaceWithJig(doc, resL.Value, origin))
            return;

        ed.Regen();
    }
}