using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class HGAction
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

                // B2: điểm đầu
                PromptPointResult startRes = _ed.GetPoint(
                    new PromptPointOptions("\n" + Lang.HG_UI_ChonDiemDau));
                if (startRes.Status != PromptStatus.OK)
                    return;

                Point3d startUcs = startRes.Value;
                Point3d startWcs = startUcs.TransformBy(_ucs);

                // B3: điểm cuối (jig ướm hình theo chiều dài)
                if (!TryGetEndPoint(startUcs, startWcs, out double length, out double axisAngle))
                    return;

                // B4: góc quay (jig ướm hình xoay quanh điểm đầu)
                if (!TryGetAngle(startUcs, length, axisAngle, out double angle))
                    return;

                // Tạo block một lần duy nhất, chèn tại điểm đầu
                ObjectId brId = HGHelper.CreateBlockReference(_db, startWcs, length, angle);
                if (brId.IsNull)
                    return;

                Finish();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(HGAction), ex);
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

        // Chọn điểm cuối bằng Jig; trả về chiều dài và góc của đường nối (theo WCS)
        private bool TryGetEndPoint(
            Point3d startUcs, Point3d startWcs, out double length, out double axisAngle)
        {
            length = 0;
            axisAngle = 0;

            HGJig endJig = HGJig.ForEndPoint(startUcs, _ucs);
            if (_ed.Drag(endJig).Status != PromptStatus.OK)
                return false;

            Vector3d dir = endJig.EndWcs - startWcs;
            length = dir.Length;

            if (length < HGHelper.LineLength)
            {
                _ed.WriteMessage("\n" + string.Format(
                    Lang.HG_MessDialog_KhoangCachPhaiLonHon, HGHelper.LineLength));
                return false;
            }

            axisAngle = Math.Atan2(dir.Y, dir.X);
            return true;
        }

        // Chọn góc quay bằng Jig: Enter = theo hướng đường nối, Esc = hủy
        private bool TryGetAngle(
            Point3d startUcs, double length, double axisAngle, out double angle)
        {
            angle = axisAngle;

            HGJig angleJig = HGJig.ForAngle(startUcs, _ucs, length, axisAngle);
            PromptResult res = _ed.Drag(angleJig);

            if (res.Status == PromptStatus.OK)
            {
                angle = angleJig.AngleWcs;
                return true;
            }

            return angleJig.NullResponse;   // Enter: dùng axisAngle; Esc: hủy
        }

        // ---------- Kết thúc ----------

        private void Finish()
        {
            _ed.Regen();
            _ed.WriteMessage("\n" + Lang.HG_MessDialog_DaTaoBlock);
        }
    }
}