using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class LiLAction
    {
        // Nhớ giá trị L của lần chạy trước
        private static double _lastL = 100.0;

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

                // B2: chọn điểm 1
                PromptPointResult p1Res = _ed.GetPoint(
                    new PromptPointOptions("\n" + Lang.LIL_UI_ChonDiemDau));
                if (p1Res.Status != PromptStatus.OK)
                    return;

                // B3: chọn điểm 2 (có đường rubber-band từ điểm 1)
                if (!TryGetSecondPoint(p1Res.Value, out Point3d p2))
                    return;

                // B4: nhập L (Enter = giá trị lần trước)
                if (!TryGetL(out double l))
                    return;

                // B5 + B6: vẽ hình.
                // GetPoint trả về theo UCS hiện hành -> LiLHelper tự đổi sang WCS
                int count = LiLHelper.DrawLiL(_db, p1Res.Value, p2, l, _ucs);

                if (count == 0)
                {
                    _ed.WriteMessage("\n" + Lang.LIL_MessDialog_HaiDiemTrungNhau);
                    return;
                }

                Finish(count);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(LiLAction), ex);
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

        private bool TryGetSecondPoint(Point3d p1, out Point3d p2)
        {
            PromptPointOptions options = new PromptPointOptions("\n" + Lang.LIL_UI_ChonDiemCuoi)
            {
                UseBasePoint = true,
                BasePoint = p1,
                UseDashedLine = true
            };

            PromptPointResult result = _ed.GetPoint(options);
            p2 = result.Status == PromptStatus.OK ? result.Value : default(Point3d);
            return result.Status == PromptStatus.OK;
        }

        // Nhập khoảng cách L, mặc định là giá trị lần trước
        private bool TryGetL(out double l)
        {
            PromptDistanceOptions options = new PromptDistanceOptions(Lang.LIL_UI_NhapKhoangCachL)
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = _lastL,
                UseDefaultValue = true
            };

            PromptDoubleResult result = _ed.GetDistance(options);
            if (result.Status != PromptStatus.OK)
            {
                l = 0;
                return false;
            }

            l = result.Value;
            _lastL = l;
            return true;
        }

        // ---------- Kết thúc ----------

        private void Finish(int count)
        {
            _ed.Regen();
            _ed.WriteMessage("\n" + string.Format(Lang.LIL_MessDialog_DaVeDoanThang, count));
        }
    }
}