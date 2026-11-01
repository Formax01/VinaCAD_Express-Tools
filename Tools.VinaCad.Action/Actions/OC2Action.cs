
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
    /// <summary>
    /// OC2: Chọn điểm -> nhập Size -> nhập L
    /// -> ướm hình, chọn góc quay -> vẽ.
    /// </summary>
    public class OC2Action
    {
        private const double DEFAULT_SIZE = 800;
        private const double DEFAULT_L = 500;

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

                // B1: Chọn điểm đặt OC2
                if (!TryGetStartPoint(out Point3d origin))
                    return;

                // B2: Nhập kích thước Size
                if (!TryGetDouble(
                    Lang.OC2_UI_NhapSize,
                    DEFAULT_SIZE,
                    out double size))
                    return;

                // B3: Nhập chiều dài L
                if (!TryGetDouble(
                    Lang.OC2_UI_NhapLengthL,
                    DEFAULT_L,
                    out double length))
                    return;

                // B4: Kiểm tra điều kiện hình học
                if (length <= size / 10)
                {
                    _ed.WriteMessage(
                        string.Format(
                            Lang.OC2_MessDialog_LPhaiLonHonSize10,
                            size / 10));
                    return;
                }

                // B5: Tạo dữ liệu hình học và chạy Jig
                List<OC1Segment> segments =
                    OC2Helper.BuildSegments(size, length);

                OC1Helper.OC1Jig jig =
                    new OC1Helper.OC1Jig(origin, _ucs, segments);

                PromptResult dragResult = _ed.Drag(jig);

                if (dragResult.Status != PromptStatus.OK)
                    return;

                // B6: Tạo các đường của OC2 trong bản vẽ
                int created = OC1Helper.CreateLines(
                    _db,
                    _ucs,
                    origin,
                    jig.Angle,
                    segments);

                Finish(created);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(OC2Action), ex);
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
        /// Giữ nguyên tọa độ UCS để Helper xử lý chuyển đổi.
        /// </summary>
        private bool TryGetStartPoint(out Point3d origin)
        {
            PromptPointOptions options =
                new PromptPointOptions(Lang.OC2_UI_ChonDiem);

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
        /// Nhập giá trị dương và sử dụng giá trị mặc định
        /// khi người dùng nhấn Enter.
        /// </summary>
        private bool TryGetDouble(
            string message,
            double defaultValue,
            out double value)
        {
            value = defaultValue;

            PromptDoubleOptions options =
                new PromptDoubleOptions(message)
                {
                    DefaultValue = defaultValue,
                    UseDefaultValue = true,
                    AllowNegative = false,
                    AllowZero = false
                };

            PromptDoubleResult result = _ed.GetDouble(options);

            if (result.Status != PromptStatus.OK)
                return false;

            value = result.Value;

            return double.IsFinite(value) && value > 0;
        }

        /// <summary>
        /// Hoàn tất lệnh và thông báo kết quả.
        /// </summary>
        private void Finish(int created)
        {
            if (created > 0)
            {
                _ed.Regen();

                _ed.WriteMessage(
                    string.Format(
                        Lang.OC2_MessDialog_DaTaoDoiTuong,
                        created));
            }
            else
            {
                _ed.WriteMessage(
                    Lang.OC2_MessDialog_KhongTheTaoDoiTuong);
            }
        }
    }
}
