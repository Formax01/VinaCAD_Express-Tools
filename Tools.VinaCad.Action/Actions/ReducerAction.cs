using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class ReducerAction
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

                ReducerModel model = new ReducerModel();

                // Bước 1: chọn điểm bắt đầu
                PromptPointResult startRes = _ed.GetPoint(Lang.B2_UI_ChonDiemBatDau);
                if (startRes.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startRes.Value;

                // Bước 2: Width1 lấy từ điểm 2 (OSNAP) hoặc nhập trực tiếp
                if (!TryGetWidth1(model.StartPoint, out double width1))
                    return;
                model.Width1 = width1;

                // Bước 3: nhập chiều dài L
                if (!TryGetPositive(Lang.B2_UI_NhapChieuCaoL, out double length))
                    return;
                model.Length = length;

                // Bước 4: kích thước đầu 2
                if (!TryGetPositive(Lang.B2_UI_NhapGiaTriDauSo2, out double width2))
                    return;
                model.Width2 = width2;

                // Bước 5: chọn chiều Giữa / Trái / Phải (mặc định Giữa)
                if (!TryGetSide(out ReducerSide side))
                    return;
                model.Side = side;

                // Bước 6: ướm chi tiết, quay hướng quanh điểm bắt đầu
                ReducerJig jig = new ReducerJig(model, _ucsToWcs, IsOrthoEnabled);
                if (_ed.Drag(jig).Status != PromptStatus.OK)
                    return;
                model.Direction = jig.Direction;

                // Bước 7: vẽ
                ReducerHelper.Draw(_db, _ucsToWcs, model);
                Finish(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ReducerAction), ex);
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

        // Width1: chọn "Điểm" (mặc định) hoặc "Nhập"
        private bool TryGetWidth1(Point3d startPoint, out double width1)
        {
            width1 = 0;

            PromptKeywordOptions options = new PromptKeywordOptions(Lang.B2_UI_ChonCachXacDinhWidth1)
            {
                AllowNone = true
            };
            options.Keywords.Add(Lang.B2_UI_KeywordDiem, Lang.B2_UI_KeywordDiem, Lang.B2_UI_KeywordDiem);
            options.Keywords.Add(Lang.B2_UI_KeywordNhap, Lang.B2_UI_KeywordNhap, Lang.B2_UI_KeywordNhap);
            options.Keywords.Default = Lang.B2_UI_KeywordDiem;

            PromptResult modeRes = _ed.GetKeywords(options);

            // Chỉ OK hoặc Enter (None) mới đi tiếp; Esc hoặc trạng thái khác thì thoát
            if (modeRes.Status != PromptStatus.OK && modeRes.Status != PromptStatus.None)
                return false;

            bool typeIn = modeRes.Status == PromptStatus.OK
                          && modeRes.StringResult == Lang.B2_UI_KeywordNhap;

            return typeIn
                ? TryGetPositive(Lang.B2_UI_NhapWidth1, out width1)
                : TryGetWidthFromPoint(startPoint, out width1);
        }

        // Chọn chiều Giữa / Trái / Phải, mặc định Giữa
        private bool TryGetSide(out ReducerSide side)
        {
            side = ReducerSide.Center;

            // global = local = display để host nào cũng đối chiếu được (click menu, gõ chữ, Enter).
            PromptKeywordOptions options = new PromptKeywordOptions(Lang.B2_UI_ChonChieu)
            {
                AllowNone = true
            };
            options.Keywords.Add(Lang.B2_UI_KeywordGiua, Lang.B2_UI_KeywordGiua, Lang.B2_UI_KeywordGiua);
            options.Keywords.Add(Lang.B2_UI_KeywordTrai, Lang.B2_UI_KeywordTrai, Lang.B2_UI_KeywordTrai);
            options.Keywords.Add(Lang.B2_UI_KeywordPhai, Lang.B2_UI_KeywordPhai, Lang.B2_UI_KeywordPhai);
            options.Keywords.Default = Lang.B2_UI_KeywordGiua;

            PromptResult res = _ed.GetKeywords(options);

            if (res.Status == PromptStatus.None)
                return true;                       // Enter không có giá trị: mặc định Giữa

            if (res.Status != PromptStatus.OK)
                return false;                      // Esc / hủy

            if (res.StringResult == Lang.B2_UI_KeywordTrai)
                side = ReducerSide.Left;
            else if (res.StringResult == Lang.B2_UI_KeywordPhai)
                side = ReducerSide.Right;

            return true;
        }

        private bool TryGetPositive(string message, out double value, Point3d? basePoint = null)
        {
            value = 0;

            PromptDistanceOptions options = new PromptDistanceOptions(message)
            {
                AllowNegative = false,
                AllowZero = false,
                Only2d = true            // chỉ tính khoảng cách trong mặt phẳng XY
            };

            if (basePoint.HasValue)
            {
                options.BasePoint = basePoint.Value;
                options.UseBasePoint = true;
                options.UseDashedLine = true;
            }

            while (true)
            {
                PromptDoubleResult res = _ed.GetDistance(options);
                if (res.Status != PromptStatus.OK)
                    return false;

                // Một số host trả về 0 dù AllowZero = false,
                // ví dụ khi điểm chọn trùng đúng điểm gốc.
                if (double.IsNaN(res.Value) || double.IsInfinity(res.Value) || res.Value <= 0)
                {
                    _ed.WriteMessage(Lang.B2_MessDialog_GiaTriPhaiLonHon0);
                    continue;
                }

                value = res.Value;
                return true;
            }
        }

        private bool TryGetWidthFromPoint(Point3d startPoint, out double width)
        {
            width = 0;

            PromptPointOptions options = new PromptPointOptions(Lang.B2_UI_ChonDiem2LayWidth1)
            {
                BasePoint = startPoint,
                UseBasePoint = true,
                UseDashedLine = true
            };

            while (true)
            {
                PromptPointResult res = _ed.GetPoint(options);
                if (res.Status != PromptStatus.OK)
                    return false;

                // Chỉ lấy khoảng cách trong mặt phẳng XY, tương tự Only2d.
                double dx = res.Value.X - startPoint.X;
                double dy = res.Value.Y - startPoint.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0)
                {
                    _ed.WriteMessage(Lang.B2_MessDialog_Diem2PhaiKhacDiemBatDau);
                    continue;
                }

                width = distance;
                return true;
            }
        }

        private static bool IsOrthoEnabled()
        {
            object v = Application.GetSystemVariable("ORTHOMODE");
            return v != null && Convert.ToInt32(v) == 1;
        }

        private void Finish(ReducerModel model)
        {
            _ed.Regen();
            _ed.WriteMessage(string.Format(
                Lang.B2_MessDialog_DaVeChiTietGiam,
                model.Length.ToString("0.##"),
                model.Width1.ToString("0.##"),
                model.Width2.ToString("0.##")));
        }
    }
}