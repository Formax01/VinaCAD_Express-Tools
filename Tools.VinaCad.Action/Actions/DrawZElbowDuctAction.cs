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
    public class DrawZElbowDuctAction
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

                ZElbowDuctModel model = new ZElbowDuctModel();

                // Bước 1: bắt điểm
                PromptPointResult startResult = _ed.GetPoint(Lang.BZ_UI_ChonDiemDat);
                if (startResult.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startResult.Value;

                // Bước 2: chiều rộng ống gió
                if (!TryGetDistance(model.StartPoint, Lang.BZ_UI_NhapChieuRongOngGio, out double width))
                    return;
                model.Width = width;

                // Bước 3: chọn bên lệch (Enter = Trái)
                if (!TryGetIsLeft(out bool isLeft))
                    return;

                // Bước 4: cao độ lượn (độ lệch giữa hai tim ống)
                if (!TryGetDistance(model.StartPoint, Lang.BZ_UI_NhapCaoDoLuonOngGio, out double offset))
                    return;
                model.Offset = isLeft ? offset : -offset;

                // Bước 5: chiều dài L (không ngắn hơn mức tối thiểu)
                if (!TryGetLength(model, out double length))
                    return;
                model.Length = length;

                // Bước 6: chọn hướng bằng Jig
                ZElbowJig jig = new ZElbowJig(model, _ucsToWcs);
                if (_ed.Drag(jig).Status != PromptStatus.OK)
                    return;
                model.Direction = jig.Direction;

                // Bước 7: vẽ hình thật
                CreateShape(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawZElbowDuctAction), ex);
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

        // Chọn bên lệch: true nếu Trái (Enter = Trái)
        private bool TryGetIsLeft(out bool isLeft)
        {
            isLeft = true;

            PromptKeywordOptions options = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            options.SetMessageAndKeywords(
                Lang.BZ_UI_ChonChieuLuon,
                $"{Lang.BZ_UI_KeywordTrai} {Lang.BZ_UI_KeywordPhai}");

            PromptResult result = _ed.GetKeywords(options);

            if (result.Status == PromptStatus.None)
                return true;                          // Enter: mặc định Trái

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            isLeft = result.StringResult != Lang.BZ_UI_KeywordPhai;
            return true;
        }

        // Chiều dài L: hỏi lại nếu ngắn hơn mức tối thiểu
        private bool TryGetLength(ZElbowDuctModel model, out double length)
        {
            double minL = ZElbowHelper.GetMinimumLength(model.Width, model.Offset);

            while (true)
            {
                string prompt = string.Format(Lang.BZ_UI_NhapChieuDaiL, minL.ToString("0.##"));
                if (!TryGetDistance(model.StartPoint, prompt, out length))
                    return false;

                if (length + 1e-9 >= minL)
                    return true;

                _ed.WriteMessage(string.Format(Lang.BZ_MessDialog_LQuaNgan, minL.ToString("0.##")));
            }
        }

        // ---------- Ghi hình ----------

        private void CreateShape(ZElbowDuctModel model)
        {
            ZElbowShape? shape = ZElbowHelper.Build(
                model.StartPoint, model.Direction,
                model.Width, model.Offset, model.Length);

            if (shape == null)
            {
                _ed.WriteMessage(Lang.BZ_MessDialog_KhongVeDuoc);
                return;
            }

            ZElbowHelper.Create(_db, shape, model.Layer, _ucsToWcs);

            Finish(model, shape);
        }

        private void Finish(ZElbowDuctModel model, ZElbowShape shape)
        {
            _ed.Regen();

            string detail = shape.Straight > 1e-6
                ? string.Format(Lang.BZ_MessDialog_ChiTiet2CutOngThang, shape.Straight.ToString("0.##"))
                : Lang.BZ_MessDialog_ChiTiet2Cut;

            _ed.WriteMessage(string.Format(
                Lang.BZ_MessDialog_DaVeOngChuZ,
                detail,
                model.Width.ToString("0.##"),
                System.Math.Abs(model.Offset).ToString("0.##"),
                model.Length.ToString("0.##"),
                (shape.Theta * 180.0 / System.Math.PI).ToString("0.#"),
                shape.Radius.ToString("0.##")));
        }
    }
}