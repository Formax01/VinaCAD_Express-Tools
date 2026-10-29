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
    public class DrawShoeAction
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

                ShoeModel model = new ShoeModel();

                // Bước 1: bắt điểm
                PromptPointResult startResult = _ed.GetPoint(Lang.B3_UI_ChonDiemDatGotGiay);
                if (startResult.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startResult.Value;

                // Bước 2: chiều cao
                if (!TryGetDistance(model.StartPoint, Lang.B3_UI_NhapChieuCao, out double height))
                    return;
                model.Height = height;

                // Bước 3: cạnh bé
                if (!TryGetDistance(model.StartPoint, Lang.B3_UI_NhapCanhBe, out double smallSide))
                    return;
                model.SmallSide = smallSide;

                // Bước 4: cạnh đáy — Enter = chiều cao + cạnh bé, gõ số = dùng số đó
                if (!TryGetBaseSide(model, out double baseSide))
                    return;
                model.BaseSide = baseSide;

                // Bước 5: chọn bên đặt cạnh đáy (Enter = Trái)
                if (!TryGetMirror(out bool mirror))
                    return;
                model.Mirror = mirror;

                // Bước 6: chọn hướng bằng Jig (gõ F để đảo bên nhanh)
                if (!TryPlaceWithJig(model))
                    return;

                // Bước 7: vẽ hình thật
                Point3d[] corners = ShoeHelper.GetCorners(
                    model.StartPoint, model.Direction,
                    model.Height, model.SmallSide, model.BaseSide, model.Mirror);

                DuctHelper.CreateClosedPolyline(_db, corners, model.Layer, _ucsToWcs);

                Finish(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawShoeAction), ex);
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

        // Cạnh đáy: Enter = tự tính, gõ số = dùng số đó
        private bool TryGetBaseSide(ShoeModel model, out double baseSide)
        {
            double autoBase = ShoeHelper.GetAutoBase(model.Height, model.SmallSide);
            baseSide = autoBase;

            PromptDistanceOptions options = new PromptDistanceOptions(Lang.B3_UI_NhapCanhDay)
            {
                BasePoint = model.StartPoint,
                UseBasePoint = true,
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true,
                DefaultValue = autoBase,
                UseDefaultValue = true
            };

            PromptDoubleResult result = _ed.GetDistance(options);

            if (result.Status == PromptStatus.None)
                return true;                          // Enter: dùng giá trị tự tính

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            baseSide = result.Value;
            return true;
        }

        // Chọn Trái / Phải: true nếu chọn Phải (Enter = Trái)
        private bool TryGetMirror(out bool mirror)
        {
            mirror = false;

            PromptKeywordOptions options = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            options.SetMessageAndKeywords(
                Lang.B3_UI_ChonBenDatCanhDay,
                $"{Lang.B3_UI_KeywordTrai} {Lang.B3_UI_KeywordPhai}");

            PromptResult result = _ed.GetKeywords(options);

            if (result.Status == PromptStatus.None)
                return true;                          // Enter: mặc định Trái

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            mirror = result.StringResult == Lang.B3_UI_KeywordPhai;
            return true;
        }

        // Ướm hướng bằng Jig; lặp lại khi người dùng gõ F để đảo bên
        private bool TryPlaceWithJig(ShoeModel model)
        {
            ShoePlacementJig jig = new ShoePlacementJig(model, _ucsToWcs);

            PromptResult dragResult;
            do
            {
                dragResult = _ed.Drag(jig);
            }
            while (jig.FlipRequested);

            if (dragResult.Status != PromptStatus.OK)
                return false;

            model.Direction = jig.Direction;
            model.Mirror = jig.Mirror;
            return true;
        }

        private void Finish(ShoeModel model)
        {
            _ed.Regen();
            _ed.WriteMessage(string.Format(
                Lang.B3_MessDialog_DaVeGotGiay,
                model.Height.ToString("0.##"),
                model.SmallSide.ToString("0.##"),
                model.BaseSide.ToString("0.##")));
        }
    }
}