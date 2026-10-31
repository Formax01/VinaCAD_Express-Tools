using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawOffsetDuctAction
    {
        private Document _doc;
        private Editor _ed;
        private Database _db;
        private Matrix3d _ucsToWcs;

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    return;

                OffsetDuctModel model = new OffsetDuctModel();

                // Bước 1: bắt điểm
                PromptPointResult startResult = _ed.GetPoint(Lang.BZ2_UI_ChonDiemDat);
                if (startResult.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startResult.Value;

                // Bước 2: chiều rộng ống gió thứ 1
                if (!TryGetDistance(model.StartPoint, Lang.BZ2_UI_NhapChieuRongOngGioThu1, out double width1))
                    return;
                model.Width1 = width1;

                // Bước 3: chiều rộng ống gió thứ 2 (Enter nếu giống ống 1)
                if (!TryGetWidth2(model, out double width2))
                    return;
                model.Width2 = width2;

                // Bước 4: chọn bên lệch (Enter = Trái)
                if (!TryGetIsLeft(out bool isLeft))
                    return;

                // Bước 5: cao độ lượn (độ lệch giữa hai tim ống)
                if (!TryGetDistance(model.StartPoint, Lang.BZ2_UI_NhapCaoDoLuonOngGio, out double offset))
                    return;
                model.Offset = isLeft ? offset : -offset;

                // Bước 6: chiều dài L (không được ngắn hơn mức tối thiểu)
                if (!TryGetLength(model, out double length))
                    return;
                model.Length = length;

                // Bước 7: chọn hướng bằng Jig
                OffsetDuctJig jig = new OffsetDuctJig(model, _ucsToWcs);
                if (_ed.Drag(jig).Status != PromptStatus.OK)
                    return;
                model.Direction = jig.Direction;

                // Bước 8: vẽ hình thật (một Polyline kín)
                Point3d[] pts = OffsetDuctHelper.GetPoints(
                    model.StartPoint, model.Direction,
                    model.Width1, model.Width2, model.Offset, model.Length);

                DuctHelper.CreateClosedPolyline(_db, pts, model.Layer, _ucsToWcs);

                Finish(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawOffsetDuctAction), ex);
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
            _ucsToWcs = _ed.CurrentUserCoordinateSystem;
        }

        // ---------- Các bước nhập ----------

        // Nhập một khoảng cách dương, có đường rubber band từ điểm đặt
        private bool TryGetDistance(Point3d basePoint, string message, out double value)
        {
            PromptDistanceOptions options = new PromptDistanceOptions(message)
            {
                BasePoint = basePoint,
                UseBasePoint = true,
                AllowNegative = false,
                AllowZero = false
            };

            PromptDoubleResult result = _ed.GetDistance(options);
            value = result.Status == PromptStatus.OK ? result.Value : 0;
            return result.Status == PromptStatus.OK;
        }

        // Chiều rộng ống 2: Enter = giống ống 1, gõ số = dùng số đó
        private bool TryGetWidth2(OffsetDuctModel model, out double width2)
        {
            width2 = model.Width1;

            PromptDistanceOptions options = new PromptDistanceOptions(Lang.BZ2_UI_NhapChieuRongOngGioThu2)
            {
                BasePoint = model.StartPoint,
                UseBasePoint = true,
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true,
                DefaultValue = model.Width1,
                UseDefaultValue = true
            };

            PromptDoubleResult result = _ed.GetDistance(options);

            if (result.Status == PromptStatus.None)
                return true;                          // Enter: giống ống 1

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            width2 = result.Value;
            return true;
        }

        // Chọn bên lệch: true nếu Trái (Enter = Trái)
        private bool TryGetIsLeft(out bool isLeft)
        {
            isLeft = true;

            PromptKeywordOptions options = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            options.SetMessageAndKeywords(
                Lang.BZ2_UI_ChonChieuLuon,
                $"{Lang.BZ2_UI_KeywordTrai} {Lang.BZ2_UI_KeywordPhai}");

            PromptResult result = _ed.GetKeywords(options);

            if (result.Status == PromptStatus.None)
                return true;                          // Enter: mặc định Trái

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            isLeft = result.StringResult != Lang.BZ2_UI_KeywordPhai;
            return true;
        }

        // Chiều dài L: hỏi lại nếu ngắn hơn mức tối thiểu
        private bool TryGetLength(OffsetDuctModel model, out double length)
        {
            double minL = OffsetDuctHelper.GetMinimumLength(
                model.Width1, model.Width2, model.Offset);

            while (true)
            {
                string prompt = string.Format(Lang.BZ2_UI_NhapChieuDaiL, minL.ToString("0.##"));
                if (!TryGetDistance(model.StartPoint, prompt, out length))
                    return false;

                if (length + 1e-9 >= minL)
                    return true;

                _ed.WriteMessage(string.Format(Lang.BZ2_MessDialog_LQuaNgan, minL.ToString("0.##")));
            }
        }

        // ---------- Kết thúc ----------

        private void Finish(OffsetDuctModel model)
        {
            _ed.Regen();
            _ed.WriteMessage(string.Format(
                Lang.BZ2_MessDialog_DaVeOngLuon,
                model.Width1.ToString("0.##"),
                model.Width2.ToString("0.##"),
                Math.Abs(model.Offset).ToString("0.##"),
                model.Length.ToString("0.##")));
        }
    }
}