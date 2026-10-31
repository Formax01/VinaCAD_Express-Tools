
using System;
using System.Collections.Generic;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using PrLogTrackingSystem;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class OC1Action
    {
        private const double DEFAULT_SIZE = 800;
        private const double DEFAULT_L = 600;
        private const double DEFAULT_l = 400;

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

                // B1: Chọn điểm đặt OC1
                if (!TryGetStartPoint(out Point3d origin))
                    return;

                // B2: Nhập kích thước Size
                if (!TryGetDouble(
                    Lang.OC1_UI_NhapSize,
                    DEFAULT_SIZE,
                    out double size))
                    return;

                // B3: Nhập chiều dài L
                if (!TryGetDouble(
                    Lang.OC1_UI_NhapLengthL,
                    DEFAULT_L,
                    out double length))
                    return;

                // B4: Nhập chiều sâu l
                if (!TryGetDouble(
                    Lang.OC1_UI_NhapLength,
                    DEFAULT_l,
                    out double depth))
                    return;

                // B5: Kiểm tra điều kiện hình học
                if (depth > length)
                {
                    _ed.WriteMessage(
                        Lang.OC1_MessDialog_LNhoHonHoacBangL);
                    return;
                }

                // B6: Tạo dữ liệu hình học và chạy Jig
                List<OC1Segment> segments =
                    OC1Helper.BuildSegments(size, length, depth);

                OC1Helper.OC1Jig jig =
                    new OC1Helper.OC1Jig(origin, _ucs, segments);

                PromptResult dragResult = _ed.Drag(jig);

                if (dragResult.Status != PromptStatus.OK)
                    return;

                // B7: Tạo dữ liệu đầu vào để vẽ
                OC1Input input = new OC1Input
                {
                    Origin = origin,
                    Size = size,
                    Length = length,
                    Depth = depth,
                    Angle = jig.Angle
                };

                // B8: Ghi các đối tượng vào bản vẽ
                int created = OC1Helper.Create(_db, _ucs, input);

                Finish(created);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(OC1Action), ex);
                throw;
            }
        }

        /// <summary>
        /// Lấy Document, Editor, Database và UCS hiện tại.
        /// </summary>
        private void UpdateCurrentDocument()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _ed = _doc.Editor;
            _db = _doc.Database;
            _ucs = _ed.CurrentUserCoordinateSystem;
        }

        /// <summary>
        /// Yêu cầu người dùng chọn điểm đặt.
        /// Giữ nguyên tọa độ điểm trong UCS để OC1Helper xử lý chuyển đổi.
        /// </summary>
        private bool TryGetStartPoint(out Point3d origin)
        {
            PromptPointOptions options =
                new PromptPointOptions(Lang.OC1_UI_ChonDiem);

            PromptPointResult result = _ed.GetPoint(options);

            if (result.Status != PromptStatus.OK)
            {
                origin = Point3d.Origin;
                return false;
            }

            origin = result.Value;
            return true;
        }

        /// <summary>
        /// Nhập một giá trị dương, cho phép nhấn Enter để dùng giá trị mặc định.
        /// </summary>
        private bool TryGetDouble(
            string message,
            double defaultValue,
            out double value)
        {
            value = defaultValue;

            if (_ed == null)
                return false;

            if (string.IsNullOrWhiteSpace(message))
                message = Lang.OC1_UI_NhapGiaTri;

            PromptDoubleOptions options =
                new PromptDoubleOptions(
                    $"{message} <{defaultValue}>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    AllowNone = true
                };

            PromptDoubleResult result = _ed.GetDouble(options);

            // Nhấn Enter: sử dụng giá trị mặc định.
            if (result.Status == PromptStatus.None)
                return true;

            if (result.Status != PromptStatus.OK)
                return false;

            value = result.Value;

            return double.IsFinite(value) && value > 0;
        }

        /// <summary>
        /// Hoàn tất sau khi OC1Helper xử lý tạo đối tượng.
        /// </summary>
        private void Finish(int created)
        {
            if (created > 0)
            {
                _ed.Regen();

                _ed.WriteMessage(
                    string.Format(
                        Lang.OC1_MessDialog_DaTaoDoiTuong,
                        created));
            }
            else
            {
                _ed.WriteMessage(
                    Lang.OC1_MessDialog_KhongTheTaoDoiTuong);
            }
        }
    }
}
