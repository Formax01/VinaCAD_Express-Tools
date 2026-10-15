using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawShoeAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            ShoeModel model = new ShoeModel();
            // Bước 1: bắt điểm
            PromptPointResult startResult =
                ed.GetPoint("\nChọn điểm đặt gót giày: ");
            if (startResult.Status != PromptStatus.OK) return;
            model.StartPoint = startResult.Value;

            // Bước 2: chiều cao
            if (!TryGetDistance(ed, model.StartPoint, "\nNhập chiều cao: ", out double height))
                return;
            model.Height = height;

            // Bước 3: cạnh bé
            if (!TryGetDistance(ed, model.StartPoint, "\nNhập cạnh bé: ", out double smallSide))
                return;
            model.SmallSide = smallSide;

            // Bước 4: cạnh đáy — Enter = chiều cao + cạnh bé, gõ số = dùng số đó
            double autoBase = ShoeHelper.GetAutoBase(model.Height, model.SmallSide);

            PromptDistanceOptions baseOpts =
                new PromptDistanceOptions("\nNhập cạnh đáy (Enter để tự tính): ")
                {
                    BasePoint = model.StartPoint,
                    UseBasePoint = true,
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true,
                    DefaultValue = autoBase,
                    UseDefaultValue = true
                };
            PromptDoubleResult baseResult = ed.GetDistance(baseOpts);

            if (baseResult.Status == PromptStatus.None)
                model.BaseSide = autoBase;
            else if (baseResult.Status == PromptStatus.OK)
                model.BaseSide = baseResult.Value;
            else
                return;
            // Bước 5: chọn bên đặt cạnh đáy (Enter = Trái)
            PromptKeywordOptions sideOpts = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            sideOpts.SetMessageAndKeywords(
                "\nChọn bên đặt cạnh đáy [Trái/Phải] <Trái>: ", "Trái Phải");

            PromptResult sideResult = ed.GetKeywords(sideOpts);

            if (sideResult.Status == PromptStatus.OK)
                model.Mirror = sideResult.StringResult == "Phải";
            else if (sideResult.Status == PromptStatus.None)
                model.Mirror = false;
            else
                return;

            // Bước 6: chọn hướng bằng Jig (gõ F để đảo bên nhanh)
            ShoePlacementJig jig = new ShoePlacementJig(model);
           
            PromptResult dragResult;
            do
            {
                dragResult = ed.Drag(jig);
            }
            while (jig.FlipRequested);

            if (dragResult.Status != PromptStatus.OK) return;

            model.Direction = jig.Direction;
            model.Mirror = jig.Mirror;

            // Vẽ hình thật
            Point3d[] corners = ShoeHelper.GetCorners(
                model.StartPoint, model.Direction,
                model.Height, model.SmallSide, model.BaseSide, model.Mirror);

            DuctHelper.CreateClosedPolyline(db, corners, model.Layer);


            ed.Regen();
            ed.WriteMessage(
                $"\nĐã vẽ gót giày: cao {model.Height:0.##}, " +
                $"cạnh bé {model.SmallSide:0.##}, cạnh đáy {model.BaseSide:0.##}.");
        }

        // Nhập một khoảng cách dương, có đường rubber band từ điểm đặt
        private static bool TryGetDistance(
            Editor ed, Point3d basePoint, string message, out double value)
        {
            PromptDistanceOptions opts = new PromptDistanceOptions(message)
            {
                BasePoint = basePoint,
                UseBasePoint = true,
                AllowNegative = false,
                AllowZero = false
            };

            PromptDoubleResult result = ed.GetDistance(opts);
            value = result.Status == PromptStatus.OK ? result.Value : 0;
            return result.Status == PromptStatus.OK;
        }
    }
}
