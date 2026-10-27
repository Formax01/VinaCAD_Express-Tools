using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
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

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // PromptPointOptions pointOpt = new PromptPointOptions("\nChọn điểm: ");
            PromptPointOptions pointOpt = new PromptPointOptions(Lang.OC1_UI_ChonDiem);
            PromptPointResult pointRes = ed.GetPoint(pointOpt);

            if (pointRes.Status != PromptStatus.OK)
                return;

            // B3, B4, B5: nhập số liệu
            if (!TryGetDouble(ed, Lang.OC1_UI_NhapSize, DEFAULT_SIZE, out double size))
                return;

            if (!TryGetDouble(ed, Lang.OC1_UI_NhapLengthL, DEFAULT_L, out double length))
                return;

            if (!TryGetDouble(ed, Lang.OC1_UI_NhapLengthl, DEFAULT_l, out double depth))
                return;

            if (depth > length)
            {
                // ed.WriteMessage("\nOC1: l phải nhỏ hơn hoặc bằng L.");
                ed.WriteMessage(Lang.OC1_MessDialog_LNhoHonHoacBangL);
                return;
            }

            // Điểm prompt và góc jig được giữ trong UCS; OC1Helper thực hiện một phép
            // quy đổi duy nhất sang WCS cho cả preview lẫn entity được ghi.
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d origin = pointRes.Value;

            List<OC1Segment> segments = OC1Helper.BuildSegments(size, length, depth);
            OC1Helper.OC1Jig jig = new OC1Helper.OC1Jig(origin, ucs, segments);

            PromptResult dragRes = ed.Drag(jig);

            if (dragRes.Status != PromptStatus.OK)
                return;

            // B7: vẽ hình
            OC1Input input = new OC1Input
            {
                Origin = origin,
                Size = size,
                Length = length,
                Depth = depth,
                Angle = jig.Angle
            };

            int created = OC1Helper.Create(db, ucs, input);

            if (created > 0)
            {
                ed.Regen();
                // ed.WriteMessage($"\nOC1: đã tạo {created} đối tượng.");
                ed.WriteMessage(string.Format(Lang.OC1_MessDialog_DaTaoDoiTuong, created));
            }
            else
            {
                ed.WriteMessage(Lang.OC1_MessDialog_KhongTheTaoDoiTuong);
            }
        }

        private static bool TryGetDouble(Editor ed, string message, double defaultValue, out double value)
        {
            value = defaultValue;

            if (ed == null)
                return false;

            if (string.IsNullOrWhiteSpace(message))
                message = "\nNhập giá trị";

            PromptDoubleOptions opt = new PromptDoubleOptions($"{message} <{defaultValue}>: ")
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true          // Enter = lấy giá trị mặc định
            };

            PromptDoubleResult res = ed.GetDouble(opt);

            if (res.Status == PromptStatus.None)
                return true;              // Enter -> giữ defaultValue

            if (res.Status != PromptStatus.OK)
                return false;

            value = res.Value;
            return double.IsFinite(value) && value > 0;
        }
    }
}
