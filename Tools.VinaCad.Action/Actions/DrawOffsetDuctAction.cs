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
    public class DrawOffsetDuctAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            OffsetDuctModel model = new OffsetDuctModel();
            // Bước 1: bắt điểm
            PromptPointResult startResult =
                ed.GetPoint("\nChọn điểm đặt : ");
            if (startResult.Status != PromptStatus.OK) return;
            model.StartPoint = startResult.Value;

            // Bước 2: chiều rộng ống gió thứ 1
            if (!TryGetDistance(ed, model.StartPoint,
                    "\nNhập chiều rộng ống gió thứ 1: ", out double w1))
                return;
            model.Width1 = w1;

            // Bước 3: chiều rộng ống gió thứ 2 (Enter nếu giống ống 1)
            PromptDistanceOptions w2Opts =
                new PromptDistanceOptions("\nNhập chiều rộng ống gió thứ 2 (Enter nếu giống ống 1): ")
                {
                    BasePoint = model.StartPoint,
                    UseBasePoint = true,
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true,
                    DefaultValue = model.Width1,
                    UseDefaultValue = true
                };
            PromptDoubleResult w2Result = ed.GetDistance(w2Opts);

            if (w2Result.Status == PromptStatus.None)
                model.Width2 = model.Width1;
            else if (w2Result.Status == PromptStatus.OK)
                model.Width2 = w2Result.Value;
            else
                return;

            // Bước 4: chọn bên lệch (Enter = Trái)
            PromptKeywordOptions sideOpts = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            sideOpts.SetMessageAndKeywords(
                "\nChọn chiều lượn [Trái/Phải] <Trái>: ", "Trái Phải");

            PromptResult sideResult = ed.GetKeywords(sideOpts);

            bool isLeft;
            if (sideResult.Status == PromptStatus.OK)
                isLeft = sideResult.StringResult != "Phải";
            else if (sideResult.Status == PromptStatus.None)
                isLeft = true;
            else
                return;

            // Bước 5: cao độ lượn (độ lệch giữa hai tim ống)
            if (!TryGetDistance(ed, model.StartPoint,
                    "\nNhập cao độ lượn của ống gió: ", out double h))
                return;
            model.Offset = isLeft ? h : -h;

            // Bước 6: chiều dài L (không được ngắn hơn mức tối thiểu)
            double minL = OffsetDuctHelper.GetMinimumLength(
                model.Width1, model.Width2, model.Offset);

            while (true)
            {
                if (!TryGetDistance(ed, model.StartPoint,
                        $"\nNhập chiều dài L cho chi tiết (tối thiểu {minL:0.##}): ", out double len))
                    return;

                if (len + 1e-9 >= minL)
                {
                    model.Length = len;
                    break;
                }

                ed.WriteMessage($"\nL quá ngắn, cần tối thiểu {minL:0.##}.");
            }

            // Bước 7: chọn hướng bằng Jig
            Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
            OffsetDuctJig jig = new OffsetDuctJig(model, ucsToWcs);
            PromptResult dragResult = ed.Drag(jig);
            if (dragResult.Status != PromptStatus.OK) return;

            model.Direction = jig.Direction;

            // Vẽ hình thật (một Polyline kín)
            Point3d[] pts = OffsetDuctHelper.GetPoints(
                model.StartPoint, model.Direction,
                model.Width1, model.Width2, model.Offset, model.Length);

            DuctHelper.CreateClosedPolyline(db, pts, model.Layer, ucsToWcs);

            ed.Regen();
            ed.WriteMessage(
                $"\nĐã vẽ ống lượn: rộng {model.Width1:0.##} / {model.Width2:0.##}, " +
                $"lượn {h:0.##}, L = {model.Length:0.##}.");
        }

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
