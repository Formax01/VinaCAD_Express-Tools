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

                // Bước 1: điểm đầu (góc phải dưới của đầu lớn)
                PromptPointResult startResult = _ed.GetPoint(Lang.B4_UI_ChonDiemDat);
                if (startResult.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startResult.Value;

                // Bước 2: chọn cách nhập kích thước đầu lớn (Enter = Length)
                if (!TryAskUsePoint(out bool usePoint))
                    return;

                // Bước 3: vẽ theo nhánh đã chọn
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

        // Nhánh Point: P1-P2 là cạnh đầu lớn, hình mở về bên phải chiều P1 -> P2
        private void DrawByPoint(SquareToRoundModel model)
        {
            Point3d p1 = model.StartPoint;

            if (!TryGetSecondPoint(p1, out Point3d p2))
                return;

            model.BigSize = p1.DistanceTo(p2);

            // n = chiều P1 -> P2; hướng đặt = n xoay -90° (để GetCorners xoay lại +90°
            // ra đúng n, khi đó a1 = P2)
            Vector3d n = (p2 - p1).GetNormal();
            model.Direction = n.RotateBy(-System.Math.PI / 2, Vector3d.ZAxis);

            if (!TryGetSmallAndLength(model))
                return;

            CreateShape(model);
        }

        // Nhánh Length: nhập đầu lớn, đầu nhỏ, L rồi ướm hướng bằng Jig
        private void DrawByLength(SquareToRoundModel model)
        {
            if (!TryGetDistance(model.StartPoint, Lang.B4_UI_NhapKichThuocDauLon, out double big))
                return;
            model.BigSize = big;

            if (!TryGetSmallAndLength(model))
                return;

            SquareToRoundJig jig = new SquareToRoundJig(model, _ucsToWcs);
            if (_ed.Drag(jig).Status != PromptStatus.OK)
                return;
            model.Direction = jig.Direction;

            CreateShape(model);
        }

        // ---------- Các bước nhập ----------

        // Chọn Point hoặc Length: true nếu chọn Point (Enter = Length)
        private bool TryAskUsePoint(out bool usePoint)
        {
            usePoint = false;

            PromptKeywordOptions options = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            options.SetMessageAndKeywords(
                Lang.B4_UI_NhapKichThuocDauLonBang,
                $"{Lang.B4_UI_KeywordPoint} {Lang.B4_UI_KeywordLength}");

            PromptResult result = _ed.GetKeywords(options);

            if (result.Status == PromptStatus.None)
                return true;                          // Enter: mặc định Length

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            usePoint = result.StringResult == Lang.B4_UI_KeywordPoint;
            return true;
        }

        // Chọn điểm 2 của đầu lớn (hỏi lại nếu trùng điểm 1)
        private bool TryGetSecondPoint(Point3d p1, out Point3d p2)
        {
            p2 = p1;

            while (true)
            {
                PromptPointOptions options = new PromptPointOptions(Lang.B4_UI_ChonDiem2DauLon)
                {
                    BasePoint = p1,
                    UseBasePoint = true,
                    UseDashedLine = true
                };

                PromptPointResult result = _ed.GetPoint(options);
                if (result.Status != PromptStatus.OK)
                    return false;

                p2 = new Point3d(result.Value.X, result.Value.Y, p1.Z);
                if (p1.DistanceTo(p2) > 1e-9)
                    return true;

                _ed.WriteMessage(Lang.B4_MessDialog_Diem2TrungDiem1);
            }
        }

        // Nhập đầu nhỏ (phải nhỏ hơn đầu lớn) rồi L (Enter để tự tính)
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

        private bool TryGetSmallSize(SquareToRoundModel model, out double small)
        {
            while (true)
            {
                if (!TryGetDistance(model.StartPoint, Lang.B4_UI_NhapKichThuocDauNho, out small))
                    return false;

                if (small < model.BigSize)
                    return true;

                _ed.WriteMessage(Lang.B4_MessDialog_DauNhoPhaiNhoHon);
            }
        }

        // Chiều dài L: Enter = tự tính, gõ số = dùng số đó
        private bool TryGetLength(SquareToRoundModel model, out double length)
        {
            double autoL = SquareToRoundHelper.GetAutoLength(model.BigSize, model.SmallSize);
            length = autoL;

            PromptDistanceOptions options = new PromptDistanceOptions(Lang.B4_UI_NhapChieuDaiL)
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
                return true;                          // Enter: dùng giá trị tự tính

            if (result.Status != PromptStatus.OK)
                return false;                         // Esc / hủy

            length = result.Value;
            return true;
        }

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

        // ---------- Ghi hình ----------

        // Ghi hình thật vào bản vẽ (dùng chung cho cả hai nhánh)
        private void CreateShape(SquareToRoundModel model)
        {
            Point3d[] corners = SquareToRoundHelper.GetCorners(
                model.StartPoint, model.Direction,
                model.BigSize, model.SmallSize, model.Length);

            SquareToRoundHelper.Create(_db, corners, model.Layer, _ucsToWcs);

            _ed.Regen();
            _ed.WriteMessage(string.Format(
                Lang.B4_MessDialog_DaVeChiTietVuongRaTron,
                model.BigSize.ToString("0.##"),
                model.SmallSize.ToString("0.##"),
                model.Length.ToString("0.##")));
        }
    }
}