using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
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

        private Document _doc;
        private Editor _ed;
        private Database _db;
        private Matrix3d _ucs;

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    return;

                if (!_setting.TryValidate(out string error))
                {
                    _ed.WriteMessage("\n" + error);
                    return;
                }

                // B2: chọn điểm đầu (tâm quay của cánh đầu tiên)
                PromptPointResult startRes = _ed.GetPoint("\n" + Lang.CLG_UI_ChonDiemDau);
                if (startRes.Status != PromptStatus.OK)
                    return;
                Point3d startUcs = startRes.Value;
                Point3d start = startUcs.TransformBy(_ucs);

                // B3: chọn điểm cuối, lấy hướng và tổng chiều dài
                if (!TryGetLine(startUcs, start, out Vector3d dir, out double total))
                    return;

                // B4: khoảng cách giữa các cánh (gõ số hoặc click 2 điểm)
                if (!TryGetSpacing(startUcs, out double spacing))
                    return;

                // B5: góc quay của từng cánh
                if (!TryGetRotation(startUcs, out double rotation))
                    return;
                rotation += GetUcsAngle();

                // Cánh đầu có tâm tại điểm đầu, phần dư để về phía điểm cuối
                int count = (int)Math.Floor(total / spacing + 1e-9) + 1;

                if (count > ConfirmCount && !TryConfirmCount(count))
                    return;

                // Vẽ
                List<Point3d> pivots = BuildPivots(start, dir, spacing, count);
                TurningVaneHelper.CreateMany(_db, _setting, pivots, rotation);

                Finish(count);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TurningVaneAction), ex);
                throw;
            }
        }

        private void UpdateCurrentDocument()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            if (_doc == null)
                return;

            _ed = _doc.Editor;
            _db = _doc.Database;
            _ucs = _ed.CurrentUserCoordinateSystem;
        }

        // ---------- Các bước nhập ----------

        // Chọn điểm cuối; trả về hướng đơn vị và tổng chiều dài (theo WCS, bỏ qua Z)
        private bool TryGetLine(Point3d startUcs, Point3d start, out Vector3d dir, out double total)
        {
            dir = default(Vector3d);
            total = 0;

            PromptPointOptions options = new PromptPointOptions("\n" + Lang.CLG_UI_ChonDiemCuoi)
            {
                UseBasePoint = true,
                BasePoint = startUcs,
                UseDashedLine = true
            };

            PromptPointResult res = _ed.GetPoint(options);
            if (res.Status != PromptStatus.OK)
                return false;

            Point3d end = res.Value.TransformBy(_ucs);

            Vector3d v = new Vector3d(end.X - start.X, end.Y - start.Y, 0);
            total = v.Length;
            if (total < 1e-9)
            {
                _ed.WriteMessage("\n" + Lang.CLG_MessDialog_DiemCuoiTrungDiemDau);
                return false;
            }

            dir = v.DivideBy(total);
            return true;
        }

        // Khoảng cách giữa các cánh: Enter = dùng giá trị lần trước
        private bool TryGetSpacing(Point3d startUcs, out double spacing)
        {
            spacing = _lastSpacing;

            PromptDistanceOptions options = new PromptDistanceOptions(
                "\n" + Lang.CLG_UI_NhapKhoangCachGiuaCacCanh)
            {
                BasePoint = startUcs,
                UseBasePoint = true,
                DefaultValue = _lastSpacing,
                UseDefaultValue = true,
                AllowNone = true,
                AllowZero = false,
                AllowNegative = false
            };

            PromptDoubleResult res = _ed.GetDistance(options);

            if (res.Status == PromptStatus.OK)
                spacing = res.Value;
            else if (res.Status != PromptStatus.None)
                return false;                         // Esc / hủy

            if (spacing <= 0)
                return false;

            _lastSpacing = spacing;
            return true;
        }

        // Nhập góc quay 0-360: Enter = dùng giá trị lần trước
        private bool TryGetRotation(Point3d basePoint, out double rotation)
        {
            rotation = _lastRotation;

            PromptAngleOptions options = new PromptAngleOptions("\n" + Lang.CLG_UI_NhapGocQuay)
            {
                BasePoint = basePoint,
                UseBasePoint = true,
                UseDashedLine = true,
                DefaultValue = _lastRotation,
                UseDefaultValue = true,
                AllowNone = true
            };

            PromptDoubleResult res = _ed.GetAngle(options);

            if (res.Status == PromptStatus.OK)
                rotation = res.Value;
            else if (res.Status != PromptStatus.None)
                return false;                         // Esc / hủy

            _lastRotation = rotation;
            return true;
        }

        // Hỏi xác nhận khi số cánh quá lớn (mặc định Không)
        private bool TryConfirmCount(int count)
        {
            PromptKeywordOptions options = new PromptKeywordOptions(
                "\n" + string.Format(Lang.CLG_UI_XacNhanVeCanh, count),
                string.Format("{0} {1}", Lang.CLG_UI_Co, Lang.CLG_UI_Khong))
            {
                AllowNone = false
            };
            options.Keywords.Default = Lang.CLG_UI_Khong;

            PromptResult res = _ed.GetKeywords(options);

            return res.Status == PromptStatus.OK
                   && string.Equals(res.StringResult, Lang.CLG_UI_Co, StringComparison.OrdinalIgnoreCase);
        }

        // ---------- Tính toán và kết thúc ----------

        // Góc của trục X của UCS so với WCS (cộng vào góc quay người dùng nhập)
        private double GetUcsAngle()
        {
            Vector3d xAxis = _ucs.CoordinateSystem3d.Xaxis;
            return Math.Atan2(xAxis.Y, xAxis.X);
        }

        private static List<Point3d> BuildPivots(Point3d start, Vector3d dir, double spacing, int count)
        {
            List<Point3d> pivots = new List<Point3d>(count);
            for (int k = 0; k < count; k++)
                pivots.Add(start + dir * (k * spacing));
            return pivots;
        }

        private void Finish(int count)
        {
            _ed.Regen();
            _ed.WriteMessage("\n" + string.Format(Lang.CLG_MessDialog_DaVeCanh, count));
        }
    }
}