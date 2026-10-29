
using System;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using PrLogTrackingSystem;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DaiOcAction
    {
        private Document _doc;
        private Editor _ed;

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    return;

                // B1: Chọn điểm đặt đai ốc
                if (!TryGetStartPoint(out Point3d startPoint))
                    return;

                // B2: Nhập chiều dài L
                if (!TryGetLength(out double length))
                    return;

                // B3: Tạo đai ốc bằng Jig
                int count = BulonHelper.CreateBolt(
                    _doc.Database,
                    _ed,
                    startPoint,
                    length,
                    BulonHelper.DAIOC_BLOCK_NAME,
                    true);

                if (count > 0)
                    Finish();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DaiOcAction), ex);
                throw;
            }
        }

        private void UpdateCurrentDocument()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            if (_doc == null)
                return;

            _ed = _doc.Editor;
        }

        // ---------- Các bước nhập ----------

        private bool TryGetStartPoint(out Point3d startPoint)
        {
            PromptPointResult result = _ed.GetPoint(
                Lang.DAIOC_UI_ChonDiemDat);

            if (result.Status != PromptStatus.OK)
            {
                startPoint = Point3d.Origin;
                return false;
            }

            startPoint = result.Value;
            return true;
        }

        private bool TryGetLength(out double length)
        {
            PromptDistanceOptions options =
                new PromptDistanceOptions(Lang.DAIOC_UI_NhapL)
                {
                    AllowNegative = false,
                    AllowZero = false
                };

            PromptDoubleResult result = _ed.GetDistance(options);

            if (result.Status != PromptStatus.OK)
            {
                length = 0;
                return false;
            }

            length = result.Value;
            return true;
        }

        // ---------- Kết thúc ----------

        private void Finish()
        {
            _ed.WriteMessage(
                string.Format(
                    Lang.DAIOC_MessDialog_DaChenBlock,
                    BulonHelper.DAIOC_BLOCK_NAME));

            _ed.Regen();
        }
    }
}
