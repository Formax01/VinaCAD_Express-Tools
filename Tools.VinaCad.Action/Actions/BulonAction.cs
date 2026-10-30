
using System;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using PrLogTrackingSystem;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using Tools.Resources.Definitions;
namespace Tools.VinaCad.Action.Actions
{
    public class BulonAction
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

                // B1: Chọn điểm đặt bu lông
                if (!TryGetStartPoint(out Point3d startPoint))
                    return;

                // B2: Nhập bán kính bu lông
                if (!TryGetRadius(out double radius))
                    return;

                // B3: Tạo bu lông
                int count = BulonHelper.CreateBolt(
                    _doc.Database, _ed, startPoint, radius);

                if (count > 0)
                    Finish();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BulonAction), ex);
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
                Lang.BULON_UI_ChonDiem);

            if (result.Status != PromptStatus.OK)
            {
                startPoint = Point3d.Origin;
                return false;
            }

            startPoint = result.Value;
            return true;
        }

        private bool TryGetRadius(out double radius)
        {
            PromptDistanceOptions options =
                new PromptDistanceOptions(
                    Lang.BULON_UI_NhapBanKinhBulon)
                {
                    AllowNegative = false,
                    AllowZero = false
                };

            PromptDoubleResult result = _ed.GetDistance(options);

            if (result.Status != PromptStatus.OK)
            {
                radius = 0;
                return false;
            }

            radius = result.Value;
            return true;
        }

        // ---------- Kết thúc ----------

        private void Finish()
        {
            _ed.WriteMessage(
                string.Format(
                    Lang.BULON_MessDialog_DaChenBlock,
                    StringDefinition.blockNameBulon1));

            _ed.Regen();
        }
    }
}
