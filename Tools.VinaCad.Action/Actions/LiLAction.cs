using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class LiLAction
    {
        // Nhớ giá trị L của lần chạy trước
        private static double _lastL = 100.0;

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // B2: nhập L
            PromptDistanceOptions optL = new PromptDistanceOptions("\nNhập khoảng cách L: ")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = _lastL,
                UseDefaultValue = true
            };

            PromptDoubleResult resL = ed.GetDistance(optL);
            if (resL.Status != PromptStatus.OK)
                return;

            double l = resL.Value;
            _lastL = l;

            // B3: chọn điểm 1
            PromptPointOptions optP1 = new PromptPointOptions("\nChọn điểm đầu: ");
            PromptPointResult resP1 = ed.GetPoint(optP1);
            if (resP1.Status != PromptStatus.OK)
                return;

            // B4: chọn điểm 2 (có đường rubber-band từ điểm 1)
            PromptPointOptions optP2 = new PromptPointOptions("\nChọn điểm cuối: ")
            {
                UseBasePoint = true,
                BasePoint = resP1.Value,
                UseDashedLine = true
            };
            PromptPointResult resP2 = ed.GetPoint(optP2);
            if (resP2.Status != PromptStatus.OK)
                return;

            // GetPoint trả về theo UCS hiện hành -> LiLHelper tự đổi sang WCS
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            // B5 + B6: vẽ hình
            int count = LiLHelper.DrawLiL(db, resP1.Value, resP2.Value, l, ucs);

            if (count == 0)
            {
                ed.WriteMessage("\nHai điểm trùng nhau, không thể vẽ.");
                return;
            }

            ed.Regen();
            ed.WriteMessage($"\nĐã vẽ {count} đoạn thẳng.");
        }
    }
}