
using System;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class BbzAction
    {
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

                // B1: Chọn điểm đầu ống gió 1
                if (!TryGetStartPoint(out Point3d startPoint))
                    return;

                // B2 -> B6: Nhập kích thước và chọn phía lượn
                if (!TryGetModel(startPoint, out BbzModel model))
                    return;

                // B7: Kiểm tra dữ liệu hình học
                if (!BbzHelper.Validate(model, out string error))
                {
                    _ed.WriteMessage(error);
                    return;
                }

                // B8: Ướm vị trí và góc xoay
                BbzPlacementJig jig = new BbzPlacementJig(model, _ucs);
                PromptResult dragResult = _ed.Drag(jig);

                if (dragResult.Status != PromptStatus.OK)
                {
                    _ed.WriteMessage(Lang.BBZ_MessDialog_DaHuy);
                    return;
                }

                // B9: Tạo khối 3D và thêm vào bản vẽ
                Matrix3d transform = BbzHelper.BuildTransform(
                    _ucs, model.StartPoint, jig.Angle);

                Solid3d solid = BbzHelper.CreateDuctSolid(model, transform);
                BbzHelper.AppendToCurrentSpace(_db, solid);

                Finish();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BbzAction), ex);
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

        // B1: Chọn điểm đầu ống gió 1
        private bool TryGetStartPoint(out Point3d startPoint)
        {
            PromptPointResult result = _ed.GetPoint(
                Lang.BBZ_UI_ChonDiemDauOng1);

            if (result.Status != PromptStatus.OK)
            {
                startPoint = Point3d.Origin;
                return false;
            }

            startPoint = result.Value;
            return true;
        }

        // B2 -> B6: Nhập kích thước và chọn phía lượn
        private bool TryGetModel(Point3d startPoint, out BbzModel model)
        {
            model = new BbzModel
            {
                StartPoint = startPoint
            };

            // B2: Bề rộng ống gió 1
            if (!TryGetDistance(
                Lang.BBZ_UI_NhapBeRongOng1,
                startPoint, null, out double width1))
                return false;

            model.Width1 = width1;

            // B3: Bề rộng ống gió 2
            // Nhấn Enter để dùng Width1 vừa nhập
            if (!TryGetDistance(
                Lang.BBZ_UI_NhapBeRongOng2,
                startPoint, width1, out double width2))
                return false;

            model.Width2 = width2;

            // B4: Chọn phía lượn
            if (!TryGetSide(out string side))
                return false;

            model.Side = side == Lang.BB2_UI_PHAI
                ? BbzSide.Right
                : BbzSide.Left;

            // B5: Cao độ lượn
            if (!TryGetDistance(
                Lang.BBZ_UI_NhapCaoDoLuon,
                startPoint, null, out double offset))
                return false;

            model.Offset = offset;

            // B6: Chiều dài L
            if (!TryGetDistance(
                Lang.BBZ_UI_NhapChieuDaiL,
                startPoint, null, out double length))
                return false;

            model.Length = length;

            // B7: Chiều cao ống gió
            if (!TryGetDistance(
                Lang.BBZ_UI_NhapChieuCaoOngGio,
                startPoint, null, out double height))
                return false;

            model.Height = height;

            return true;
        }

        private bool TryGetSide(out string side)
        {
            PromptKeywordOptions options =
                new PromptKeywordOptions(
                    Lang.BBZ_UI_ChonPhiaLuon, $"{Lang.BB2_UI_Trai} {Lang.BB2_UI_PHAI}")
                {
                    AllowNone = true
                };

            options.Keywords.Default = Lang.BB2_UI_Trai;

            PromptResult result = _ed.GetKeywords(options);

            if (result.Status != PromptStatus.OK
                && result.Status != PromptStatus.None)
            {
                side = Lang.BB2_UI_Trai;
                return false;
            }

            side = result.StringResult == Lang.BB2_UI_PHAI
                ? Lang.BB2_UI_PHAI
                : Lang.BB2_UI_Trai;

            return true;
        }

        private bool TryGetDistance(
            string message,
            Point3d basePoint,
            double? defaultValue,
            out double value)
        {
            value = 0;

            PromptDistanceOptions options =
                new PromptDistanceOptions(message)
                {
                    AllowNegative = false,
                    AllowZero = false,
                    UseBasePoint = true,
                    BasePoint = basePoint
                };

            if (defaultValue.HasValue)
            {
                options.AllowNone = true;
                options.DefaultValue = defaultValue.Value;
                options.UseDefaultValue = true;
            }

            PromptDoubleResult result = _ed.GetDistance(options);

            // Enter: dùng giá trị mặc định nếu có
            if (result.Status == PromptStatus.None
                && defaultValue.HasValue)
            {
                value = defaultValue.Value;
                return true;
            }

            if (result.Status != PromptStatus.OK)
                return false;

            value = result.Value;
            return true;
        }

        // ---------- Kết thúc ----------

        private void Finish()
        {
            _ed.Regen();
            _ed.WriteMessage(
                Lang.BBZ_MessDialog_DaVeOngGioChuZ3D);
        }
    }
}
