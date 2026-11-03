
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using Lang = Tools.Resources.Language.Lang;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawSquareToRoundAction
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

                SquareToRoundModel model = new SquareToRoundModel();

                // Bước 1: Chọn điểm đặt
                PromptPointResult startResult =
                    _ed.GetPoint(Lang.B4_UI_ChonDiemDat);

                if (startResult.Status != PromptStatus.OK)
                    return;

                model.StartPoint = startResult.Value;

                // Bước 2: Nhập kích thước đầu lớn hoặc nhấn P
                if (!TryGetBigSizeOrPoint(model, out bool usePoint))
                    return;

                // Bước 3: Chọn phương pháp vẽ
                if (usePoint)
                    DrawByPoint(model);
                else
                    DrawByLength(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawSquareToRoundAction), ex);
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

        // ---------- Hai nhánh vẽ ----------

        // Nhánh Point: chọn P2 để xác định BigSize và Direction
        private void DrawByPoint(SquareToRoundModel model)
        {
            Point3d p1 = model.StartPoint;

            if (!TryGetSecondPoint(p1, out Point3d p2))
                return;

            model.BigSize = p1.DistanceTo(p2);

            Vector3d n = (p2 - p1).GetNormal();

            model.Direction = n.RotateBy(
                -Math.PI / 2,
                Vector3d.ZAxis);

            if (!TryGetSmallAndLength(model))
                return;

            CreateShape(model);
        }

        // Nhánh Length: BigSize đã được nhập ở bước lựa chọn
        private void DrawByLength(SquareToRoundModel model)
        {
            // Không hỏi lại BigSize

            if (!TryGetSmallAndLength(model))
                return;

            SquareToRoundJig jig =
                new SquareToRoundJig(model, _ucsToWcs);

            if (_ed.Drag(jig).Status != PromptStatus.OK)
                return;

            model.Direction = jig.Direction;

            CreateShape(model);
        }

        // ---------- Lựa chọn phương pháp vẽ ----------

        // Nhập kích thước đầu lớn hoặc nhấn P để chọn điểm
        // true  = chọn Point
        // false = nhập Length
        private bool TryGetBigSizeOrPoint(
            SquareToRoundModel model,
            out bool usePoint)
        {
            usePoint = false;

            PromptDistanceOptions options =
                new PromptDistanceOptions(
                    $"{Lang.B4_UI_NhapKichThuocDauLon} " +
                    $"[{Lang.B4_UI_KeywordPoint}]: ",
                    Lang.B4_UI_KeywordPoint)
                {
                    AllowNegative = false,
                    AllowZero = false,
                    BasePoint = model.StartPoint,
                    UseBasePoint = true
                };

            PromptDoubleResult result = _ed.GetDistance(options);

            // Người dùng nhấn P
            if (result.Status == PromptStatus.Keyword)
            {
                if (result.StringResult == Lang.B4_UI_KeywordPoint)
                {
                    usePoint = true;
                    return true;
                }

                return false;
            }

            // Người dùng nhập kích thước
            if (result.Status == PromptStatus.OK)
            {
                model.BigSize = result.Value;
                usePoint = false;
                return true;
            }

            // ESC hoặc hủy thao tác
            return false;
        }

        // ---------- Các bước nhập ----------

        // Chọn điểm 2 của đầu lớn
        // Hỏi lại nếu P1 và P2 trùng nhau
        private bool TryGetSecondPoint(Point3d p1, out Point3d p2)
        {
            p2 = p1;

            while (true)
            {
                PromptPointOptions options =
                    new PromptPointOptions(
                        Lang.B4_UI_ChonDiem2DauLon)
                    {
                        BasePoint = p1,
                        UseBasePoint = true,
                        UseDashedLine = true
                    };

                PromptPointResult result = _ed.GetPoint(options);

                if (result.Status != PromptStatus.OK)
                    return false;

                p2 = new Point3d(
                    result.Value.X,
                    result.Value.Y,
                    p1.Z);

                if (p1.DistanceTo(p2) > 1e-9)
                    return true;

                _ed.WriteMessage(
                    Lang.B4_MessDialog_Diem2TrungDiem1);
            }
        }

        // Nhập đầu nhỏ rồi nhập chiều dài L
        private bool TryGetSmallAndLength(SquareToRoundModel model)
        {
            if (!TryGetSmallSize(model, out double small))
                return false;

            model.SmallSize = small;

            if (!TryGetLength(model, out double length))
                return false;

            model.Length = length;

            return true;
        }

        // Đầu nhỏ phải nhỏ hơn đầu lớn
        private bool TryGetSmallSize(
            SquareToRoundModel model,
            out double small)
        {
            while (true)
            {
                if (!TryGetDistance(
                    model.StartPoint,
                    Lang.B4_UI_NhapKichThuocDauNho,
                    out small))
                {
                    return false;
                }

                if (small < model.BigSize)
                    return true;

                _ed.WriteMessage(
                    Lang.B4_MessDialog_DauNhoPhaiNhoHon);
            }
        }

        // Chiều dài L:
        // Enter = tự tính
        // Nhập số = sử dụng giá trị nhập
        private bool TryGetLength(
            SquareToRoundModel model,
            out double length)
        {
            double autoL = SquareToRoundHelper.GetAutoLength(
                model.BigSize,
                model.SmallSize);

            length = autoL;

            PromptDistanceOptions options =
                new PromptDistanceOptions(
                    Lang.B4_UI_NhapChieuDaiL)
                {
                    BasePoint = model.StartPoint,
                    UseBasePoint = true,
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true,
                    DefaultValue = autoL,
                    UseDefaultValue = true
                };

            PromptDoubleResult result = _ed.GetDistance(options);

            if (result.Status == PromptStatus.None)
                return true;

            if (result.Status != PromptStatus.OK)
                return false;

            length = result.Value;
            return true;
        }

        // Nhập một khoảng cách dương
        private bool TryGetDistance(
            Point3d basePoint,
            string message,
            out double value)
        {
            PromptDistanceOptions options =
                new PromptDistanceOptions(message)
                {
                    BasePoint = basePoint,
                    UseBasePoint = true,
                    AllowNegative = false,
                    AllowZero = false
                };

            PromptDoubleResult result = _ed.GetDistance(options);

            value = result.Status == PromptStatus.OK
                ? result.Value
                : 0;

            return result.Status == PromptStatus.OK;
        }

        // ---------- Ghi hình ----------

        // Dùng chung cho cả hai nhánh
        private void CreateShape(SquareToRoundModel model)
        {
            Point3d[] corners = SquareToRoundHelper.GetCorners(
                model.StartPoint,
                model.Direction,
                model.BigSize,
                model.SmallSize,
                model.Length);

            SquareToRoundHelper.Create(
                _db,
                corners,
                model.Layer,
                _ucsToWcs);

            _ed.Regen();

            _ed.WriteMessage(string.Format(
                Lang.B4_MessDialog_DaVeChiTietVuongRaTron,
                model.BigSize.ToString("0.##"),
                model.SmallSize.ToString("0.##"),
                model.Length.ToString("0.##")));
        }
    }
}
