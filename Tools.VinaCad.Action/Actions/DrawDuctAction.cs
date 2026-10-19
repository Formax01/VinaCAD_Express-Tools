
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawDuctAction
    {
        public void Execute()
        {
            Document doc =
                Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            DuctModel model = new DuctModel();

            // Bước 1: Chọn điểm bắt đầu
            PromptPointResult startResult =
                ed.GetPoint("\nChọn điểm bắt đầu ống gió: ");

            if (startResult.Status != PromptStatus.OK)
                return;

            model.StartPoint = startResult.Value;

            // Bước 2: Chọn phương thức vẽ
            PromptKeywordOptions modeOptions =
                new PromptKeywordOptions(
                    "\nChọn cách vẽ [Nhập/Điểm]: ")
                {
                    AllowNone = false
                };

            modeOptions.Keywords.Add("Nhập");
            modeOptions.Keywords.Add("Điểm");

            PromptResult modeResult = ed.GetKeywords(modeOptions);

            if (modeResult.Status != PromptStatus.OK)
                return;

            // Bước 3: Điều phối đến phương thức vẽ tương ứng
            if (string.Equals(
                modeResult.StringResult,
                "Nhập",
                StringComparison.OrdinalIgnoreCase))
            {
                DrawByLength(ed, db, model);
            }
            else if (string.Equals(
                modeResult.StringResult,
                "Điểm",
                StringComparison.OrdinalIgnoreCase))
            {
                DrawByTwoPoints(ed, db, model);
            }
        }

        // Nhánh 1: Vẽ bằng hai điểm
        private void DrawByTwoPoints(
            Editor ed,
            Database db,
            DuctModel model)
        {
            Point3d p1 = model.StartPoint;

            // Chọn điểm 2 để xác định chiều dài và hướng cạnh
            PromptPointOptions pointOptions =
                new PromptPointOptions(
                    "\nChọn điểm 2 để xác định chiều dài và hướng cạnh: ")
                {
                    BasePoint = p1,
                    UseBasePoint = true
                };

            PromptPointResult pointResult =
                ed.GetPoint(pointOptions);

            if (pointResult.Status != PromptStatus.OK)
                return;

            Point3d p2 = new Point3d(
                pointResult.Value.X,
                pointResult.Value.Y,
                p1.Z);

            // Kiểm tra hai điểm có trùng nhau không
            if (p1.DistanceTo(p2) < 1e-9)
            {
                ed.WriteMessage("\nĐiểm 2 trùng điểm 1.");
                return;
            }

            // Kéo chuột để xác định phía và chiều rộng
            Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
            DuctSideJig sideJig =
                new DuctSideJig(p1, p2, ucsToWcs);

            PromptResult dragResult =
                ed.Drag(sideJig);

            if (dragResult.Status != PromptStatus.OK)
                return;

            // Tính bốn góc của ống gió
            Point3d[] corners =
                DuctHelper.GetCornersFromSide(
                    p1,
                    p2,
                    sideJig.Offset);

            // Tạo Polyline trong bản vẽ
            DuctHelper.CreateClosedPolyline(
                db,
                corners,
                model.Layer,
                ucsToWcs);

            ed.Regen();

            ed.WriteMessage(
                $"\nĐã vẽ ống gió " +
                $"{p1.DistanceTo(p2):0.##} x " +
                $"{Math.Abs(sideJig.Offset):0.##}.");
        }

        // Nhánh 2: Vẽ bằng chiều dài và chiều rộng
        private void DrawByLength(
            Editor ed,
            Database db,
            DuctModel model)
        {
            // Bước 1: Nhập chiều dài
            while (true)
            {
                PromptStringOptions lengthOptions =
                    new PromptStringOptions(
                        "\nNhập chiều dài ống gió: ")
                    {
                        AllowSpaces = false
                    };

                PromptResult lengthResult =
                    ed.GetString(lengthOptions);

                if (lengthResult.Status != PromptStatus.OK)
                    return;

                string input =
                    (lengthResult.StringResult ?? "")
                    .Trim()
                    .Replace(',', '.');

                if (double.TryParse(
                        input,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double length)
                    && length > 1e-9)
                {
                    model.Length = length;
                    break;
                }

                ed.WriteMessage(
                    "\nChiều dài không hợp lệ. Hãy nhập số dương.");
            }

            // Bước 2: Nhập chiều rộng
            PromptDistanceOptions widthOptions =
                new PromptDistanceOptions(
                    "\nNhập chiều rộng ống gió: ")
                {
                    AllowNegative = false,
                    AllowZero = false
                };

            PromptDoubleResult widthResult =
                ed.GetDistance(widthOptions);

            if (widthResult.Status != PromptStatus.OK)
                return;

            model.Width = widthResult.Value;

            // Bước 3: Kéo chuột để xác định hướng đặt ống gió
            Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
            DuctPlacementJig jig =
                new DuctPlacementJig(model, ucsToWcs);

            PromptResult placementResult =
                ed.Drag(jig);

            if (placementResult.Status != PromptStatus.OK)
                return;

            model.Direction = jig.Direction;

            // Bước 4: Tạo ống gió
            DuctHelper.CreateDuct(db, model, ucsToWcs);

            ed.Regen();

            ed.WriteMessage(
                $"\nĐã vẽ ống gió " +
                $"{model.Length:0.##} x " +
                $"{model.Width:0.##}.");
        }
    }
}
