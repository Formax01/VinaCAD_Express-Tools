using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;
using Tools.ViewModel;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions;

public sealed class FlexDuctAction
{
    public void Execute(FlexDuctType type)
    {
        Document? document = Application.DocumentManager.MdiActiveDocument;
        if (document == null) return;
        var vm = new FlexDuctVM { DuctType = type };
        Editor editor = document.Editor;

        PromptDoubleResult diameter = editor.GetDistance(new PromptDistanceOptions($"\nĐường kính ống mềm <{vm.Diameter}>: ")
        { UseDefaultValue = true, DefaultValue = vm.Diameter });
        
        if (diameter.Status != PromptStatus.OK || diameter.Value <= 0) return;
        vm.Diameter = diameter.Value;

        var modeOptions = new PromptKeywordOptions("\nChọn cách lấy đường tâm [Draw/Select] <Draw>: ") { AllowNone = true };
       
        modeOptions.Keywords.Add("Draw", "D", "Draw");
        modeOptions.Keywords.Add("Select", "S", "Select");
        
        PromptResult mode = editor.GetKeywords(modeOptions);
        if (mode.Status == PromptStatus.Cancel) return;
        vm.PathMode = mode.StringResult.Equals("Select", StringComparison.OrdinalIgnoreCase)
            ? FlexDuctPathMode.Select : FlexDuctPathMode.Draw;

        List<Point3d>? path = vm.PathMode == FlexDuctPathMode.Select ? SelectPath(editor, document.Database) : DrawPath(editor, document.Database);
        if (path == null) return;

        string name = type.ToString();
        FlexDuctStyle style = ResolveStyle(type);

        (int count, string groupName, string? warning) =
            FlexDuctHelper.Draw(document.Database, path, vm.Model, style);
        editor.UpdateScreen();
        if (count == 0) { editor.WriteMessage("\nKhông thể vẽ ống với đường tâm đã chọn."); return; }
        if (warning != null) editor.WriteMessage($"\n[Cảnh báo] {warning}");
        editor.WriteMessage($"\n{name}: đã vẽ {count} đối tượng trong group {groupName}.");
    }


    private static FlexDuctStyle ResolveStyle(FlexDuctType type) => type switch
    {
        FlexDuctType.TopDown => FlexDuctStyles.S1(),
        FlexDuctType.Horizontal => FlexDuctStyles.S2(),
        FlexDuctType.DoubleTopDown => FlexDuctStyles.S3(),
        FlexDuctType.TopDownBare => FlexDuctStyles.R1(),   
        FlexDuctType.HorizontalBare => FlexDuctStyles.R2(),   
        FlexDuctType.DoubleTopDownBare => FlexDuctStyles.R3(),   
        _ => FlexDuctStyles.S1()
    };

    private static List<Point3d>? DrawPath(Editor editor, Database database)
    {
        var path = new List<Point3d>();
        using var preview = new FlexDuctPathPreview(database);   

        while (true)
        {
            var options = new PromptPointOptions(path.Count == 0
                ? "\nChọn điểm đầu đường tâm: " : "\nChọn điểm tiếp theo <Enter kết thúc>: ")
            { AllowNone = path.Count > 0 };
            if (path.Count > 0) { options.BasePoint = path[^1]; options.UseBasePoint = true; }
            PromptPointResult result = editor.GetPoint(options);
            if (result.Status == PromptStatus.None && path.Count > 1) return path;
            if (result.Status != PromptStatus.OK) return null;
            if (path.Count == 0 || result.Value.DistanceTo(path[^1]) > 1e-9)
            {
                path.Add(result.Value);
                preview.Update(path);       
                editor.UpdateScreen();      
            }
        }
    }

    private static List<Point3d>? SelectPath(Editor editor, Database database)
    {
        var options = new PromptEntityOptions("\nChọn Line/Polyline đường tâm: ");
        options.SetRejectMessage("\nChỉ nhận Line hoặc Polyline.");
        options.AddAllowedClass(typeof(Line), true);
        options.AddAllowedClass(typeof(Polyline), true);
        PromptEntityResult result = editor.GetEntity(options);
        if (result.Status != PromptStatus.OK) return null;

        using Transaction transaction = database.TransactionManager.StartTransaction();
        Entity entity = (Entity)transaction.GetObject(result.ObjectId, OpenMode.ForRead);
        bool ok = FlexDuctHelper.TryGetPath(entity, out List<Point3d> path);
        transaction.Commit();
        return ok ? path : null;
    }
}