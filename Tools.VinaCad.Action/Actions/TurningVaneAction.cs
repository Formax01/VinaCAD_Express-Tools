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
    public class TurningVaneAction
    {
        private const int ConfirmCount = 500;   

        private static readonly TurningVaneSetting _setting = new TurningVaneSetting();
        private static double _lastRotation = 0;     
        private static double _lastSpacing = 150;

        private Editor _ed;
        private Database _db;

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            _ed = doc.Editor;
            _db = doc.Database;
            Matrix3d ucs = _ed.CurrentUserCoordinateSystem;
            if (!_setting.TryValidate(out string error))
            {
                _ed.WriteMessage("\n" + error);
                return;
            }

            // B2: chọn điểm đầu (tâm quay của cánh đầu tiên)
            PromptPointResult startRes = _ed.GetPoint("\nChọn điểm đầu : ");
            if (startRes.Status != PromptStatus.OK) return;
            Point3d startUcs = startRes.Value;
            Point3d start = startUcs.TransformBy(ucs);

            // B3: chọn điểm cuối
            var endOpt = new PromptPointOptions("\nChọn điểm cuối: ")
            {
                UseBasePoint = true,
                BasePoint = startUcs,
                UseDashedLine = true
            };
            PromptPointResult endRes = _ed.GetPoint(endOpt);
            if (endRes.Status != PromptStatus.OK) return;
            Point3d end = endRes.Value.TransformBy(ucs);

            var v = new Vector3d(end.X - start.X, end.Y - start.Y, 0);
            double total = v.Length;
            if (total < 1e-9)
            {
                _ed.WriteMessage("\nĐiểm cuối trùng điểm đầu.");
                return;
            }
            Vector3d dir = v.DivideBy(total);

            // B4: khoảng cách giữa các đối tượng (gõ số hoặc click 2 điểm)
            var distOpt = new PromptDistanceOptions("\nNhập khoảng cách giữa các cánh")
            {
                BasePoint = startUcs,
                UseBasePoint = true,
                DefaultValue = _lastSpacing,
                UseDefaultValue = true,
                AllowNone = true,
                AllowZero = false,
                AllowNegative = false
            };
            PromptDoubleResult distRes = _ed.GetDistance(distOpt);
            double spacing;
            if (distRes.Status == PromptStatus.OK) spacing = distRes.Value;
            else if (distRes.Status == PromptStatus.None) spacing = _lastSpacing;
            else return;
            if (spacing <= 0) return;
            _lastSpacing = spacing;

            // B5: góc quay của từng cánh
            if (!PromptRotation(startUcs, out double rotation)) return;
            Vector3d ucsXAxis = ucs.CoordinateSystem3d.Xaxis;
            rotation += Math.Atan2(ucsXAxis.Y, ucsXAxis.X);

            // Cánh đầu có tâm tại điểm đầu, phần dư để về phía điểm cuối
            int count = (int)Math.Floor(total / spacing + 1e-9) + 1;
            double remainder = total - (count - 1) * spacing;

            if (count > ConfirmCount)
            {
                var kw = new PromptKeywordOptions($"\nSẽ vẽ {count} cánh, tiếp tục? [Co/Khong]", "Co Khong")
                { AllowNone = false };
                kw.Keywords.Default = "Khong";
                PromptResult kr = _ed.GetKeywords(kw);
                if (kr.Status != PromptStatus.OK || kr.StringResult != "Co") return;
            }

            var pivots = new List<Point3d>(count);
            for (int k = 0; k < count; k++)
                pivots.Add(start + dir * (k * spacing));

            TurningVaneHelper.CreateMany(_db, _setting, pivots, rotation);
            _ed.Regen();
            _ed.WriteMessage($"\nĐã vẽ {count} cánh lượn gió.");
        }
        // ---------- Nhập góc quay 0-360 ----------
        private bool PromptRotation(Point3d basePoint, out double rotation)
        {
            var opt = new PromptAngleOptions("\nNhập góc quay (0-360)")
            {
                BasePoint = basePoint,
                UseBasePoint = true,
                UseDashedLine = true,
                DefaultValue = _lastRotation,
                UseDefaultValue = true,
                AllowNone = true
            };

            PromptDoubleResult res = _ed.GetAngle(opt);

            if (res.Status == PromptStatus.OK) rotation = res.Value;
            else if (res.Status == PromptStatus.None) rotation = _lastRotation;
            else { rotation = _lastRotation; return false; }   // hủy

            _lastRotation = rotation;
            return true;
        }


    }
}