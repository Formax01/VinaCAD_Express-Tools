using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class ReducerAction
    {
        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            var model = new ReducerModel();

            // Bước 1: chọn điểm bắt đầu
            PromptPointResult startRes = ed.GetPoint("\nChọn điểm bắt đầu chi tiết giảm: ");
            if (startRes.Status != PromptStatus.OK) return;
            model.StartPoint = startRes.Value;

            // Bước 2: Width1 có thể lấy từ điểm 2 (OSNAP) hoặc nhập trực tiếp.
            var widthOpts = new PromptKeywordOptions("\nChọn cách xác định Width1")
            {
                AllowNone = true
            };
            widthOpts.Keywords.Add("Điểm", "Điểm", "Điểm");
            widthOpts.Keywords.Add("Nhập", "Nhập", "Nhập");
            widthOpts.Keywords.Default = "Điểm";

            PromptResult widthModeRes = ed.GetKeywords(widthOpts);
            if (widthModeRes.Status == PromptStatus.Cancel) return;

            double width1;
            if (widthModeRes.Status == PromptStatus.OK && widthModeRes.StringResult == "Nhập")
            {
                if (!TryGetPositive(ed, "\nNhập Width1: ", out width1)) return;
            }
            else if (widthModeRes.Status == PromptStatus.OK || widthModeRes.Status == PromptStatus.None)
            {
                if (!TryGetWidthFromPoint(ed, model.StartPoint, out width1)) return;
            }
            else
            {
                return;
            }
            model.Width1 = width1;

            // Bước 3: nhập chiều dài L
            if (!TryGetPositive(ed, "\nNhập chiều cao L: ", out double length)) return;
            model.Length = length;

            // Bước 4: kích thước đầu 2
            if (!TryGetPositive(ed, "\nNhập giá trị đầu số 2: ", out double w2)) return;
            model.Width2 = w2;

            // Bước 5: chọn chiều Giữa / Trái / Phải (mặc định Giữa)
            // global = local = display để host nào cũng đối chiếu được (click menu, gõ chữ, Enter).
            var sideOpts = new PromptKeywordOptions("\nChọn chiều")
            {
                AllowNone = true
            };
            sideOpts.Keywords.Add("Giữa", "Giữa", "Giữa");
            sideOpts.Keywords.Add("Trái", "Trái", "Trái");
            sideOpts.Keywords.Add("Phải", "Phải", "Phải");
            sideOpts.Keywords.Default = "Giữa";

            PromptResult sideRes = ed.GetKeywords(sideOpts);

            if (sideRes.Status == PromptStatus.OK)
            {
                model.Side = sideRes.StringResult switch
                {
                    "Trái" => ReducerSide.Left,
                    "Phải" => ReducerSide.Right,
                    _ => ReducerSide.Center
                };
            }
            else if (sideRes.Status == PromptStatus.None)
            {
                // Enter không có giá trị: lấy mặc định là Giữa
                model.Side = ReducerSide.Center;
            }
            else
            {
                return; // Esc / hủy
            }

            // Bước 6: ướm chi tiết, quay hướng quanh điểm bắt đầu
            var jig = new ReducerJig(model, ed.CurrentUserCoordinateSystem, IsOrthoEnabled);
            PromptResult dragRes = ed.Drag(jig);
            if (dragRes.Status != PromptStatus.OK) return;

            model.Direction = jig.Direction;

            // Bước 7: vẽ
            ReducerHelper.Draw(db, ed.CurrentUserCoordinateSystem, model);
            ed.Regen();
            ed.WriteMessage(
                $"\nĐã vẽ chi tiết giảm dài {model.Length:0.##}, " +
                $"kích thước {model.Width1:0.##} → {model.Width2:0.##}.");
        }

        private static bool TryGetPositive(
    Editor ed, string message, out double value, Point3d? basePoint = null)
        {
            value = 0;
            var opts = new PromptDistanceOptions(message)
            {
                AllowNegative = false,
                AllowZero = false,
                Only2d = true            // chỉ tính khoảng cách trong mặt phẳng XY
            };

            if (basePoint.HasValue)
            {
                opts.BasePoint = basePoint.Value;
                opts.UseBasePoint = true;
                opts.UseDashedLine = true;
            }

            while (true)
            {
                PromptDoubleResult res = ed.GetDistance(opts);
                if (res.Status != PromptStatus.OK) return false;

                // Some CAD hosts can return zero despite AllowZero=false, for
                // example when the picked point is exactly the base point.
                if (double.IsNaN(res.Value) || double.IsInfinity(res.Value) || res.Value <= 0)
                {
                    ed.WriteMessage("\nGiá trị phải lớn hơn 0. Vui lòng nhập lại hoặc chọn điểm khác.");
                    continue;
                }

                value = res.Value;
                return true;
            }
        }

        private static bool TryGetWidthFromPoint(Editor ed, Point3d startPoint, out double width)
        {
            width = 0;
            var opts = new PromptPointOptions("\nChọn điểm 2 để lấy Width1: ")
            {
                BasePoint = startPoint,
                UseBasePoint = true,
                UseDashedLine = true
            };

            while (true)
            {
                PromptPointResult res = ed.GetPoint(opts);
                if (res.Status != PromptStatus.OK) return false;

                // Chỉ lấy khoảng cách trong mặt phẳng XY, tương tự Only2d.
                double dx = res.Value.X - startPoint.X;
                double dy = res.Value.Y - startPoint.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0)
                {
                    ed.WriteMessage("\nĐiểm 2 phải khác điểm bắt đầu. Vui lòng chọn lại.");
                    continue;
                }

                width = distance;
                return true;
            }
        }

        private static bool IsOrthoEnabled()
        {
            object v = Application.GetSystemVariable("ORTHOMODE");
            return v != null && Convert.ToInt32(v) == 1;
        }
    }
}
