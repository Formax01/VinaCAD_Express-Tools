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
    public class VCDAction
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

                // Bước 1: chọn điểm bắt đầu
                PromptPointResult startRes = _ed.GetPoint(
                    new PromptPointOptions("\n" + Lang.VCD_UI_ChonDiemBatDau));
                if (startRes.Status != PromptStatus.OK)
                    return;
                Point3d start = startRes.Value;

                // Bước 2: nhập Length và Width
                if (!TryGetPositive(Lang.VCD_UI_NhapLength, out double length))
                    return;

                if (!TryGetPositive(Lang.VCD_UI_NhapWidth, out double width))
                    return;

                if (length < width)
                {
                    _ed.WriteMessage("\n" + Lang.VCD_MessDialog_LengthPhaiLonHonHoacBangWidth);
                    return;
                }

                // Bước 3: Jig ướm hình, xoay quanh góc dưới trái
                VCDJig jig = new VCDJig(_db, start, length, width, _ucsToWcs);
                if (_ed.Drag(jig).Status != PromptStatus.OK)
                    return;

                // Bước 4: vẽ hình thật
                int count = VCDHelper.CreateShape(_db, start, length, width, jig.Rotation, _ucsToWcs);

                Finish(count);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(VCDAction), ex);
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

        // Nhập một số dương (không âm, không bằng 0)
        private bool TryGetPositive(string message, out double value)
        {
            PromptDoubleOptions options = new PromptDoubleOptions("\n" + message)
            {
                AllowNegative = false,
                AllowZero = false
            };

            PromptDoubleResult result = _ed.GetDouble(options);
            value = result.Status == PromptStatus.OK ? result.Value : 0;
            return result.Status == PromptStatus.OK;
        }

        // ---------- Kết thúc ----------

        private void Finish(int count)
        {
            _ed.Regen();
            _ed.WriteMessage("\n" + string.Format(Lang.VCD_MessDialog_DaVeHinhChuNhatVaBlock, count));
        }
    }
}