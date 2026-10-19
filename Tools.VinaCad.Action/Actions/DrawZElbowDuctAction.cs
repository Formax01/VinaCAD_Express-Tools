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
    public class DrawZElbowDuctAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            ZElbowDuctModel model = new ZElbowDuctModel();
            // Bước 1: bắt điểm 
            PromptPointResult startResult =
                ed.GetPoint("\nChọn điểm đặt : ");
            if (startResult.Status != PromptStatus.OK) return;
            model.StartPoint = startResult.Value;

            // Bước 2: chiều rộng ống gió
            if (!TryGetDistance(ed, model.StartPoint,
                    "\nNhập chiều rộng ống gió: ", out double w))
                return;
            model.Width = w;

            // Bước 3: chọn bên lệch (Enter = Trái)
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

            // Bước 4: cao độ lượn (độ lệch giữa hai tim ống)
            if (!TryGetDistance(ed, model.StartPoint,
                    "\nNhập cao độ lượn của ống gió: ", out double h))
                return;
            model.Offset = isLeft ? h : -h;

            // Bước 5: chiều dài L (không ngắn hơn mức tối thiểu)
            double minL = ZElbowHelper.GetMinimumLength(model.Width, model.Offset);

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

            // Bước 6: chọn hướng bằng Jig
            Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
            ZElbowJig jig = new ZElbowJig(model, ucsToWcs);
            PromptResult dragResult = ed.Drag(jig);
            if (dragResult.Status != PromptStatus.OK) return;

            model.Direction = jig.Direction;

            // Vẽ hình thật
            ZElbowShape? shape = ZElbowHelper.Build(
                model.StartPoint, model.Direction,
                model.Width, model.Offset, model.Length);

            if (shape == null)
            {
                ed.WriteMessage("\nKhông vẽ được với các thông số này.");
                return;
            }

            ZElbowHelper.Create(db, shape, model.Layer, ucsToWcs);

            ed.Regen();

            string detail = shape.Straight > 1e-6
                ? $"2 cút + ống thẳng {shape.Straight:0.##}"
                : "2 cút";
            ed.WriteMessage(
                $"\nĐã vẽ ống chữ Z ({detail}): rộng {model.Width:0.##}, " +
                $"lượn {h:0.##}, L = {model.Length:0.##}, góc cút {shape.Theta * 180.0 / System.Math.PI:0.#}°, " +
                $"R tim {shape.Radius:0.##}.");
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
