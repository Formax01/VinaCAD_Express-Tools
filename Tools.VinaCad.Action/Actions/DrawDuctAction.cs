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
        private enum DrawMode { Length, TwoPoints }

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

                DuctModel model = new DuctModel();

                // Bước 1: Chọn điểm bắt đầu
                PromptPointResult startResult = _ed.GetPoint(Lang.B1_UI_ChonDiemBatDauOngGio);
                if (startResult.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startResult.Value;

                // Bước 2: Chọn phương thức vẽ
                if (!TryAskDrawMode(out DrawMode mode))
                    return;

                // Bước 3: Điều phối đến phương thức vẽ tương ứng
                if (mode == DrawMode.TwoPoints)
                    DrawByTwoPoints(model);
                else
                    DrawByLength(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawDuctAction), ex);
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

        // Nhánh 1: Vẽ bằng hai điểm (P1-P2 là một cạnh, kéo vuông góc để lấy chiều rộng)
        private void DrawByTwoPoints(DuctModel model)
        {
            Point3d p1 = model.StartPoint;

            // Chọn điểm 2 để xác định chiều dài và hướng cạnh
            PromptPointOptions pointOptions = new PromptPointOptions(Lang.B1_UI_ChonDiem2)
            {
                BasePoint = p1,
                UseBasePoint = true
            };
            PromptPointResult pointResult = _ed.GetPoint(pointOptions);
            if (pointResult.Status != PromptStatus.OK)
                return;

            Point3d p2 = new Point3d(pointResult.Value.X, pointResult.Value.Y, p1.Z);

            // Kiểm tra hai điểm có trùng nhau không
            if (p1.DistanceTo(p2) < 1e-9)
            {
                _ed.WriteMessage(Lang.B1_MessDialog_Diem2TrungDiem1);
                return;
            }

            // Kéo chuột để xác định phía và chiều rộng
            DuctSideJig sideJig = new DuctSideJig(p1, p2, _ucsToWcs);
            if (_ed.Drag(sideJig).Status != PromptStatus.OK)
                return;

            // Tính bốn góc rồi tạo Polyline
            Point3d[] corners = DuctHelper.GetCornersFromSide(p1, p2, sideJig.Offset);
            DuctHelper.CreateClosedPolyline(_db, corners, model.Layer, _ucsToWcs);

            Finish(p1.DistanceTo(p2), Math.Abs(sideJig.Offset));
        }

        // Nhánh 2: Vẽ bằng chiều dài và chiều rộng
        private void DrawByLength(DuctModel model)
        {
            // Bước 1: Nhập chiều dài
            if (!TryGetLength(out double length))
                return;
            model.Length = length;

            // Bước 2: Nhập chiều rộng
            PromptDistanceOptions widthOptions = new PromptDistanceOptions(Lang.B1_UI_NhapChieuRongOngGio)
            {
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult widthResult = _ed.GetDistance(widthOptions);
            if (widthResult.Status != PromptStatus.OK)
                return;
            model.Width = widthResult.Value;

            // Bước 3: Kéo chuột để xác định hướng đặt ống gió
            DuctPlacementJig jig = new DuctPlacementJig(model, _ucsToWcs);
            if (_ed.Drag(jig).Status != PromptStatus.OK)
                return;
            model.Direction = jig.Direction;

            // Bước 4: Tạo ống gió
            DuctHelper.CreateDuct(_db, model, _ucsToWcs);

            Finish(model.Length, model.Width);
        }

        

        private bool TryAskDrawMode(out DrawMode mode)
        {
            mode = DrawMode.Length;

            PromptKeywordOptions options = new PromptKeywordOptions(Lang.B1_UI_ChonCachVe)
            {
                AllowNone = false
            };
            options.Keywords.Add(Lang.B1_UI_KeywordNhap);
            options.Keywords.Add(Lang.B1_UI_KeywordDiem);

            PromptResult result = _ed.GetKeywords(options);
            if (result.Status != PromptStatus.OK)
                return false;

            bool isTwoPoints = string.Equals(
                result.StringResult,
                Lang.B1_UI_KeywordDiem,
                StringComparison.OrdinalIgnoreCase);

            mode = isTwoPoints ? DrawMode.TwoPoints : DrawMode.Length;
            return true;
        }

        // GetString giữ nguyên số gõ vào và cho phép dấu phẩy thập phân
        private bool TryGetLength(out double length)
        {
            length = 0;

            while (true)
            {
                PromptStringOptions options = new PromptStringOptions(Lang.B1_UI_NhapChieuDaiOngGio)
                {
                    AllowSpaces = false
                };

                PromptResult result = _ed.GetString(options);
                if (result.Status != PromptStatus.OK)
                    return false;

                string input = (result.StringResult ?? "").Trim().Replace(',', '.');

                if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out length)
                    && length > 1e-9)
                    return true;

                _ed.WriteMessage(Lang.B1_MessDialog_ChieuDaiKhongHopLe);
            }
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