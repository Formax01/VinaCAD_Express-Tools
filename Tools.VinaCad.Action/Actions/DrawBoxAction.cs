using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawBoxAction
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

                // Bước 1: chọn điểm bắt đầu
                PromptPointResult startRes = _ed.GetPoint(Lang.BB1_UI_ChonDiemBatDau);
                if (startRes.Status != PromptStatus.OK)
                    return;
                Point3d start = startRes.Value;

                // Bước 2: nhập Length, Width, Height
                if (!TryGetSize(Lang.BB1_UI_NhapLength, out double length))
                    return;
                if (!TryGetSize(Lang.BB1_UI_NhapWidth, out double width))
                    return;
                if (!TryGetSize(Lang.BB1_UI_NhapHeight, out double height))
                    return;

                // Bước 3: Jig ướm hình, xoay quanh điểm bắt đầu
                DrawBoxJig jig = new DrawBoxJig(_ucs, start, length, width, height);
                if (_ed.Drag(jig).Status != PromptStatus.OK)
                {
                    _ed.WriteMessage(Lang.BB1_MessDialog_DaHuy);
                    return;
                }

                // Bước 4: vẽ hình thật
                DrawBoxHelper.CreateBox(_db, _ucs, start, length, width, height, jig.Angle);

                Finish(length, width, height);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawBoxAction), ex);
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

        // Nhập một số dương (không âm, không bằng 0, không cho bỏ trống)
        private bool TryGetSize(string message, out double value)
        {
            value = 0;

            PromptDoubleOptions options = new PromptDoubleOptions(message)
            {
                AllowZero = false,
                AllowNegative = false,
                AllowNone = false
            };

            PromptDoubleResult result = _ed.GetDouble(options);
            if (result.Status != PromptStatus.OK)
                return false;

            value = result.Value;
            return true;
        }

        // ---------- Kết thúc ----------

        private void Finish(double length, double width, double height)
        {
            _ed.Regen();
            _ed.WriteMessage(string.Format(Lang.BB1_MessDialog_DaVeOngGio, length, width, height));
        }
    }
}