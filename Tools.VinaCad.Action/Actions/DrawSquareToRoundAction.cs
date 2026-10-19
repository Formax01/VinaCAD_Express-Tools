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
    public class DrawSquareToRoundAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            SquareToRoundModel model = new SquareToRoundModel();
            // Bước 1: điểm đầu (góc phải dưới của đầu lớn)
            PromptPointResult startResult =
                ed.GetPoint("\nChọn điểm đặt (góc phải dưới của đầu lớn): ");
            if (startResult.Status != PromptStatus.OK) return;
            model.StartPoint = startResult.Value;

            // Bước 2: chọn cách nhập kích thước đầu lớn (Enter = Length)
            PromptKeywordOptions modeOpts = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            modeOpts.SetMessageAndKeywords(
                "\nNhập kích thước đầu lớn bằng [Point/Length] <Length>: ",
                "Point Length");

            PromptResult modeResult = ed.GetKeywords(modeOpts);

            bool usePoint;
            if (modeResult.Status == PromptStatus.OK)
                usePoint = modeResult.StringResult == "Point";
            else if (modeResult.Status == PromptStatus.None)
                usePoint = false;
            else
                return;

            if (usePoint)
                DrawByPoint(ed, db, model);
            else
                DrawByLength(ed, db, model);
        }

        // Nhánh Point: P1-P2 là cạnh đầu lớn, hình mở về bên phải chiều P1 -> P2
        private void DrawByPoint(Editor ed, Database db, SquareToRoundModel model)
        {
            Point3d p1 = model.StartPoint;
            Point3d p2;

            while (true)
            {
                PromptPointOptions p2Opts =
                    new PromptPointOptions("\nChọn điểm 2 của đầu lớn: ")
                    {
                        BasePoint = p1,
                        UseBasePoint = true,
                        UseDashedLine = true
                    };
                PromptPointResult p2Result = ed.GetPoint(p2Opts);
                if (p2Result.Status != PromptStatus.OK) return;

                p2 = new Point3d(p2Result.Value.X, p2Result.Value.Y, p1.Z);
                if (p1.DistanceTo(p2) > 1e-9) break;

                ed.WriteMessage("\nĐiểm 2 trùng điểm 1.");
            }

            model.BigSize = p1.DistanceTo(p2);

            // n = chiều P1 -> P2; hướng đặt = n xoay -90° (để GetSquareToRoundCorners
            // xoay lại +90° ra đúng n, khi đó a1 = P2)
            Vector3d n = (p2 - p1).GetNormal();
            model.Direction = n.RotateBy(-System.Math.PI / 2, Vector3d.ZAxis);

            if (!TryGetSmallAndLength(ed, model)) return;

            CreateShape(ed, db, model, ed.CurrentUserCoordinateSystem);
        }

        // Nhánh Length: nhập đầu lớn, đầu nhỏ, L rồi ướm hướng bằng Jig
        private void DrawByLength(Editor ed, Database db, SquareToRoundModel model)
        {
            if (!TryGetDistance(ed, model.StartPoint, "\nNhập kích thước đầu lớn: ", out double big))
                return;
            model.BigSize = big;

            if (!TryGetSmallAndLength(ed, model)) return;

            Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
            SquareToRoundJig jig = new SquareToRoundJig(model, ucsToWcs);
            PromptResult dragResult = ed.Drag(jig);
            if (dragResult.Status != PromptStatus.OK) return;

            model.Direction = jig.Direction;

            CreateShape(ed, db, model, ucsToWcs);
        }

        // Nhập đầu nhỏ (phải nhỏ hơn đầu lớn) và L (Enter để tự tính)
        private bool TryGetSmallAndLength(Editor ed, SquareToRoundModel model)
        {
            double small;
            while (true)
            {
                if (!TryGetDistance(ed, model.StartPoint, "\nNhập kích thước đầu nhỏ: ", out small))
                    return false;

                if (small < model.BigSize) break;
                ed.WriteMessage("\nĐầu nhỏ phải nhỏ hơn đầu lớn.");
            }
            model.SmallSize = small;

            double autoL = SquareToRoundHelper.GetAutoLength(model.BigSize, model.SmallSize);

            PromptDistanceOptions lengthOpts =
                new PromptDistanceOptions("\nNhập chiều dài L (Enter để tự tính): ")
                {
                    BasePoint = model.StartPoint,
                    UseBasePoint = true,
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true,
                    DefaultValue = autoL,
                    UseDefaultValue = true
                };
            PromptDoubleResult lengthResult = ed.GetDistance(lengthOpts);

            if (lengthResult.Status == PromptStatus.None)
                model.Length = autoL;
            else if (lengthResult.Status == PromptStatus.OK)
                model.Length = lengthResult.Value;
            else
                return false;

            return true;
        }

        // Ghi hình thật vào bản vẽ (dùng chung cho cả hai nhánh)
        private void CreateShape(Editor ed, Database db, SquareToRoundModel model, Matrix3d ucsToWcs)
        {
            Point3d[] corners = SquareToRoundHelper.GetCorners(
                model.StartPoint, model.Direction,
                model.BigSize, model.SmallSize, model.Length);

            SquareToRoundHelper.Create(db, corners, model.Layer, ucsToWcs);

            ed.Regen();
            ed.WriteMessage(
                $"\nĐã vẽ chi tiết vuông ra tròn: đầu lớn {model.BigSize:0.##}, " +
                $"đầu nhỏ {model.SmallSize:0.##}, L = {model.Length:0.##}.");
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
