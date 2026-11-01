
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
    public class DaiOc2Action
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
                if (!TryGetStartPoint(out Point3d origin))
                    return;

                // B2: Nhập chiều dài L
                if (!TryGetLength(out double length))
                    return;

                // B3: Ướm bằng Jig và đặt đai ốc
                if (!DaiOc2Helper.PlaceWithJig(_doc, length, origin))
                    return;

                Finish();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DaiOc2Action), ex);
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

        private bool TryGetStartPoint(out Point3d origin)
        {
            PromptPointResult result = _ed.GetPoint(
                new PromptPointOptions(
                    Lang.DAIOC2_UI_ChonDiemDat));

            if (result.Status != PromptStatus.OK)
            {
                origin = Point3d.Origin;
                return false;
            }

            origin = result.Value.TransformBy(
                _ed.CurrentUserCoordinateSystem);

            return true;
        }

        private bool TryGetLength(out double length)
        {
            PromptDoubleOptions options =
                new PromptDoubleOptions(Lang.DAIOC2_UI_NhapL)
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 500,
                    UseDefaultValue = true
                };

            PromptDoubleResult result = _ed.GetDouble(options);

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
            _ed.Regen();
        }
    }
}
