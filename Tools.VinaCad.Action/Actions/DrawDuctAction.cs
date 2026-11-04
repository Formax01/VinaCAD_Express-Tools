using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawDuctAction
    {
        private enum DrawMode
        {
            Length,
            TwoPoints
        }

        private const double Tolerance = 1e-9;

        private Document _doc;
        private Editor _ed;
        private Database _db;
        private Matrix3d _ucsToWcs;

        public void Execute()
        {
            try
            {
                if (!UpdateCurrentDocument())
                    return;

                var model = new DuctModel();

                // 1. Start point
                var startResult = _ed.GetPoint(Lang.B1_UI_ChonDiemBatDauOngGio);
                if (startResult.Status != PromptStatus.OK)
                    return;

                model.StartPoint = startResult.Value;

                // 2. Length or "P" for two-point mode
                if (!TryAskDrawMode(out DrawMode mode, out double length))
                    return;

                // 3. Dispatch
                if (mode == DrawMode.TwoPoints)
                {
                    DrawByTwoPoints(model);
                }
                else
                {
                    model.Length = length;
                    DrawByLength(model);
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawDuctAction), ex);
                throw;
            }
        }

        private bool UpdateCurrentDocument()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            if (_doc == null)
                return false;

            _ed = _doc.Editor;
            _db = _doc.Database;
            _ucsToWcs = _ed.CurrentUserCoordinateSystem;
            return true;
        }

        /// <summary>
        /// Draw by two points (P1-P2 defines one side).
        /// User then drags perpendicular to set the width/offset.
        /// </summary>
        private void DrawByTwoPoints(DuctModel model)
        {
            Point3d p1 = model.StartPoint;

            var pointOptions = new PromptPointOptions(Lang.B1_UI_ChonDiem2)
            {
                BasePoint = p1,
                UseBasePoint = true
            };

            var pointResult = _ed.GetPoint(pointOptions);
            if (pointResult.Status != PromptStatus.OK)
                return;

            // Keep same Z as start point
            Point3d p2 = new Point3d(pointResult.Value.X, pointResult.Value.Y, p1.Z);

            if (p1.DistanceTo(p2) < Tolerance)
            {
                _ed.WriteMessage(Lang.B1_MessDialog_Diem2TrungDiem1);
                return;
            }

            var sideJig = new DuctSideJig(p1, p2, _ucsToWcs);
            if (_ed.Drag(sideJig).Status != PromptStatus.OK)
                return;

            Point3d[] corners = DuctHelper.GetCornersFromSide(p1, p2, sideJig.Offset);
            DuctHelper.CreateClosedPolyline(_db, corners, model.Layer, _ucsToWcs);

            Finish(p1.DistanceTo(p2), Math.Abs(sideJig.Offset));
        }

        /// <summary>
        /// Draw by explicit length + width, then place with a jig.
        /// </summary>
        private void DrawByLength(DuctModel model)
        {
            var widthOptions = new PromptDistanceOptions(Lang.B1_UI_NhapChieuRongOngGio)
            {
                AllowNegative = false,
                AllowZero = false
            };

            var widthResult = _ed.GetDistance(widthOptions);
            if (widthResult.Status != PromptStatus.OK)
                return;

            model.Width = widthResult.Value;

            var jig = new DuctPlacementJig(model, _ucsToWcs);
            if (_ed.Drag(jig).Status != PromptStatus.OK)
                return;

            model.Direction = jig.Direction;
            DuctHelper.CreateDuct(_db, model, _ucsToWcs);

            Finish(model.Length, model.Width);
        }

        private bool TryAskDrawMode(out DrawMode mode, out double length)
        {
            mode = DrawMode.Length;
            length = 0;

            while (true)
            {
                var options = new PromptDoubleOptions(Lang.B1_UI_NhapChieuDaiHoacP)
                {
                    AllowNegative = false,
                    AllowZero = false,
                    AllowArbitraryInput = true,       // "1,5" không phải số chuẩn: CAD trả về chuỗi để mình đổi dấu phẩy
                    AppendKeywordsToMessage = true    // hiện [P] trên dòng nhắc
                };
                options.Keywords.Add(
                    Lang.B1_UI_KeywordP, Lang.B1_UI_KeywordP, Lang.B1_UI_KeywordP);

                PromptDoubleResult result = _ed.GetDouble(options);

                switch (result.Status)
                {
                    case PromptStatus.Keyword:          // gõ P hoặc chọn P từ menu mũi tên xuống
                        mode = DrawMode.TwoPoints;
                        return true;

                    case PromptStatus.OK:               // gõ số chuẩn: 500, 1.5
                        if (result.Value > Tolerance)
                        {
                            mode = DrawMode.Length;
                            length = result.Value;
                            return true;
                        }
                        break;

                    case PromptStatus.Other:            // chuỗi khác, ví dụ "1,5"
                        if (TryParseLength(result.StringResult, out length))
                        {
                            mode = DrawMode.Length;
                            return true;
                        }
                        break;

                    default:                            // Esc
                        return false;
                }

                _ed.WriteMessage(Lang.B1_MessDialog_ChieuDaiKhongHopLe);
            }
        }

        private static bool TryParseLength(string text, out double length)
        {
            length = 0;

            string input = (text ?? string.Empty)
                .Trim()
                .Replace(',', '.');

            return double.TryParse(
                       input,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out length)
                   && length > Tolerance;
        }

        private void Finish(double length, double width)
        {
            _ed.Regen();
            _ed.WriteMessage(string.Format(
                Lang.B1_MessDialog_DaVeOngGio,
                length.ToString("0.##"),
                width.ToString("0.##")));
        }
    }
}