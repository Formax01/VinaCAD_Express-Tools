using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    /// <summary>OC2: chọn điểm -> Size (800) -> L (500) -> ướm hình / chọn góc quay -> vẽ.</summary>
    public class OC2Action
    {
        private const double DEFAULT_SIZE = 800;
        private const double DEFAULT_L = 500;

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult pointRes = ed.GetPoint(new PromptPointOptions("\nChọn điểm: "));

            if (pointRes.Status != PromptStatus.OK)
                return;

            if (!TryGetDouble(ed, "\nNhập Size", DEFAULT_SIZE, out double size))
                return;

            if (!TryGetDouble(ed, "\nNhập Length L", DEFAULT_L, out double length))
                return;

            if (length <= size / 10)
            {
                ed.WriteMessage($"\nOC2: L phải lớn hơn Size/10 ({size / 10}).");
                return;
            }

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d origin = pointRes.Value;

            List<OC1Segment> segments = OC2Helper.BuildSegments(size, length);
            OC1Helper.OC1Jig jig = new OC1Helper.OC1Jig(origin, ucs, segments);

            PromptResult dragRes = ed.Drag(jig);

            if (dragRes.Status != PromptStatus.OK)
                return;

            int created = OC1Helper.CreateLines(db, ucs, origin, jig.Angle, segments);

            if (created > 0)
            {
                ed.Regen();
                ed.WriteMessage($"\nOC2: đã tạo {created} đối tượng.");
            }
            else
            {
                ed.WriteMessage("\nOC2: không thể tạo đối tượng; bản vẽ không bị thay đổi.");
            }
        }

        private static bool TryGetDouble(Editor ed, string message, double defaultValue, out double value)
        {
            value = defaultValue;

            PromptDoubleOptions opt = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false
            };

            PromptDoubleResult res = ed.GetDouble(opt);

            if (res.Status != PromptStatus.OK)
                return false;

            value = res.Value;
            return double.IsFinite(value) && value > 0;
        }
    }
}
