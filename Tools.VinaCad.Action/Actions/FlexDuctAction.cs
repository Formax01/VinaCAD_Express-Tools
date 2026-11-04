using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.Resources.Language;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions;

public sealed class FlexDuctAction
{
    public void Execute(FlexDuctType type)
    {
        try
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            var vm = new FlexDuctVM { DuctType = type };
            Editor editor = document.Editor;

            var distanceOptions = new PromptDistanceOptions("\n" + Lang.FlexDuct_Prompt_DuongKinhOngMem.Replace("{diameter}", vm.Diameter.ToString()));
            distanceOptions.AllowNone = true;
            PromptDoubleResult diameter = editor.GetDistance(distanceOptions);

            if (diameter.Status == PromptStatus.None)
            {
            }
            else if (diameter.Status == PromptStatus.OK && diameter.Value > 0)
            {
                vm.Diameter = diameter.Value;
            }
            else
            {
                return;
            }

            var modeOptions = new PromptKeywordOptions("\n" + Lang.FlexDuct_Prompt_CachLayDuongTam)
            {
                AllowNone = true,
                AppendKeywordsToMessage = false
            };
            modeOptions.Keywords.Add("Draw", Lang.FlexDuct_Keyword_Ve, Lang.FlexDuct_Keyword_Ve);
            modeOptions.Keywords.Add("Select", Lang.FlexDuct_Keyword_Chon, Lang.FlexDuct_Keyword_Chon);

            PromptResult mode = editor.GetKeywords(modeOptions);
            if (mode.Status != PromptStatus.OK && mode.Status != PromptStatus.None) return;
            vm.PathMode = mode.Status == PromptStatus.OK &&
                (string.Equals(mode.StringResult, "Select", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mode.StringResult, Lang.FlexDuct_Keyword_Chon, StringComparison.OrdinalIgnoreCase))
                ? FlexDuctPathMode.Select : FlexDuctPathMode.Draw;

            ObjectId sourceId = ObjectId.Null;          // line/polyline gốc (chỉ có khi chọn có sẵn)
            List<Point3d>? path = vm.PathMode == FlexDuctPathMode.Select
                ? SelectPath(editor, document.Database, out sourceId)
                : DrawPath(editor, document.Database);
            if (path == null) return;

            string name = type switch
            {
                FlexDuctType.TopDown => "S1",
                FlexDuctType.Horizontal => "S2",
                FlexDuctType.DoubleTopDown => "S3",
                FlexDuctType.TopDownBare => "R1",
                FlexDuctType.HorizontalBare => "R2",
                FlexDuctType.DoubleTopDownBare => "R3",
                _ => type.ToString()
            };
            FlexDuctStyle style = ResolveStyle(type);

            (int count, string groupName, string? warning) =
                FlexDuctHelper.Draw(document.Database, path, vm.Model, style);
            editor.UpdateScreen();
            if (count == 0) { editor.WriteMessage("\n" + (warning ?? Lang.FlexDuct_Output_KhongTheVeOng)); return; }
            if (!sourceId.IsNull) EraseSource(editor, document.Database, sourceId);
            if (warning != null) editor.WriteMessage("\n" + Lang.FlexDuct_Output_CanhBao.Replace("{warning}", warning));
            editor.WriteMessage("\n" + Lang.FlexDuct_Output_DaVe.Replace("{commandName}", name).Replace("{count}", count.ToString()).Replace("{groupName}", groupName));
        }
        catch (System.Exception ex)
        {
            Logger.Info(nameof(FlexDuctAction), ex);
            throw new Exception(ex.Message, ex);
        }
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
                ? "\n" + Lang.FlexDuct_Prompt_ChonDiemDauDuongTam : "\n" + Lang.FlexDuct_Prompt_ChonDiemTiepTheo)
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

    private static List<Point3d>? SelectPath(Editor editor, Database database, out ObjectId sourceId)
    {
        sourceId = ObjectId.Null;

        var options = new PromptEntityOptions("\n" + Lang.FlexDuct_Prompt_ChonLinePolylineDuongTam);
        options.SetRejectMessage("\n" + Lang.FlexDuct_Prompt_ChiNhanLinePolyline);
        options.AddAllowedClass(typeof(Line), true);
        options.AddAllowedClass(typeof(Polyline), true);
        PromptEntityResult result = editor.GetEntity(options);
        if (result.Status != PromptStatus.OK) return null;

        using Transaction transaction = database.TransactionManager.StartTransaction();
        Entity entity = (Entity)transaction.GetObject(result.ObjectId, OpenMode.ForRead);
        bool ok = FlexDuctHelper.TryGetPath(entity, out List<Point3d> path);
        transaction.Commit();

        if (ok) sourceId = result.ObjectId;     // nhớ lại để xóa sau khi vẽ xong
        return ok ? path : null;
    }

    private static void EraseSource(Editor editor, Database database, ObjectId id)
    {
        try
        {
            using Transaction transaction = database.TransactionManager.StartTransaction();
            Entity entity = (Entity)transaction.GetObject(id, OpenMode.ForWrite);
            entity.Erase();
            transaction.Commit();
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage("\n" + Lang.FlexDuct_Output_KhongXoaDuongTamGoc.Replace("{ex.Message}", ex.Message));
        }
    }

}