using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.GraphicsInterface;
using Teigha.Geometry;
using Tools.Model;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DuctBranchAction
    {
        public void Execute(DuctBranchType type)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Editor editor = doc.Editor;
                Database db = doc.Database;

                DuctBranchVM vm = DuctBranchVM.Get(type);

                // Lưu giá trị cũ để khôi phục nếu dữ liệu không hợp lệ
                double oldW = vm.MainWidth, oldH = vm.MainHeight;
                double oldWs = vm.ThroughWidth, oldHs = vm.ThroughHeight;
                double oldWb = vm.BranchWidth, oldHb = vm.BranchHeight, oldR = vm.Radius;
                double oldWb2 = vm.Branch2Width, oldR2 = vm.Radius2;
                ElbowSide oldSide = vm.Side;

                // 1) Điểm đặt
                PromptPointResult ppr = editor.GetPoint(new PromptPointOptions("\nChọn điểm đặt: "));
                if (ppr.Status != PromptStatus.OK) return;
                Point3d insert = ppr.Value;

                // 2) Ống chính: bề rộng + chiều cao (đoạn thẳng trước chỗ rẽ)
                double w;
                if (!AskDouble(editor, "Nhập đường kính (bề rộng) ống chính W", vm.MainWidth, out w)) return;
                vm.MainWidth = w;

                double h;
                if (!AskDouble(editor, "Nhập chiều cao ống chính H (đoạn thẳng trước chỗ rẽ)", vm.MainHeight, out h, true)) return;
                vm.MainHeight = h;

                // 3) Ống đi thẳng đi tiếp (eex, eez): kích thước đầu ra độc lập với ống rẽ
                if (vm.NeedsThrough)
                {
                    double ws;
                    if (!AskDouble(editor, "Nhập đường kính ống đi thẳng đi tiếp Ws", vm.SuggestThroughWidth(), out ws)) return;
                    vm.ThroughWidth = ws;

                    double hs;
                    if (!AskDouble(editor, "Nhập chiều cao ống đi tiếp Hs (đoạn thẳng vượt qua miệng nhánh)", vm.ThroughHeight, out hs, true)) return;
                    vm.ThroughHeight = hs;
                }

                // 4) Nhánh 1 (eex: nhánh cong duy nhất, eey: cả 2 nhánh, eez: nhánh trái)
                string n1 = vm.NeedsBranch2 ? "1" : "";
                double wb;
                if (!AskDouble(editor, "Nhập đường kính ống rẽ Wb" + n1, vm.SuggestBranchWidth(), out wb)) return;
                vm.BranchWidth = wb;

                double r;
                if (!AskDouble(editor, "Nhập bán kính cong nhánh rẽ R" + n1, vm.SuggestRadius(wb), out r)) return;
                vm.Radius = r;

                // 5) Nhánh 2 (chỉ eez)
                if (vm.NeedsBranch2)
                {
                    double wb2;
                    if (!AskDouble(editor, "Nhập đường kính ống rẽ Wb2", vm.Branch2Width, out wb2)) return;
                    vm.Branch2Width = wb2;

                    double r2;
                    if (!AskDouble(editor, "Nhập bán kính cong nhánh rẽ R2", vm.SuggestRadius(wb2), out r2)) return;
                    vm.Radius2 = r2;
                }

                // 6) Chiều cao ống rẽ (đoạn thẳng sau cung rẽ, dùng chung các nhánh)
                double hb;
                if (!AskDouble(editor, "Nhập chiều cao ống rẽ Hb (đoạn thẳng sau cung rẽ)", vm.BranchHeight, out hb, true)) return;
                vm.BranchHeight = hb;

                // 7) Hướng rẽ (chỉ eex có 1 nhánh cong)
                if (vm.NeedsSide)
                {
                    string s;
                    if (!AskKeyword(editor, "Chọn hướng rẽ", new[] { "Left", "Right" },
                            vm.Side == ElbowSide.Left ? "Left" : "Right", out s)) return;
                    vm.Side = s == "Right" ? ElbowSide.Right : ElbowSide.Left;
                }

                // Kiểm tra hợp lệ
                string err;
                if (!Validate(vm, out err))
                {
                    vm.MainWidth = oldW; vm.MainHeight = oldH;
                    vm.ThroughWidth = oldWs; vm.ThroughHeight = oldHs;
                    vm.BranchWidth = oldWb; vm.BranchHeight = oldHb; vm.Radius = oldR;
                    vm.Branch2Width = oldWb2; vm.Radius2 = oldR2;
                    vm.Side = oldSide;
                    editor.WriteMessage("\n" + err);
                    return;
                }

                // 8) Hướng đặt: jig rê chuột xem trước, click để chốt
                Vector3d direction;
                var jig = new DuctBranchPreviewJig(insert, type.ToString(), currentDirection => vm.BuildModel(insert, currentDirection));
                PromptResult result = editor.Drag(jig);
                if (result.Status != PromptStatus.OK) return;

                direction = jig.Direction;
                if (direction.Length < 1e-6)
                {
                    editor.WriteMessage("\nHướng ống không hợp lệ.");
                    return;
                }

                DuctBranchModel model = vm.BuildModel(insert, direction);
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DuctBranchHelper.Draw(db, tr, model);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLỗi: " + ex.Message);
            }
        }

        private static bool Validate(DuctBranchVM vm, out string error)
        {
            error = null;
            double w = vm.MainWidth;
            double wb1 = vm.BranchWidth;
            double wb2 = vm.EffectiveBranch2Width;

            if (w <= 0) { error = "Đường kính ống chính W phải lớn hơn 0."; return false; }
            if (vm.MainHeight < 0) { error = "Chiều cao ống chính H không được âm."; return false; }
            if (vm.BranchHeight < 0) { error = "Chiều cao ống rẽ Hb không được âm."; return false; }

            if (wb1 <= 0) { error = "Đường kính ống rẽ Wb phải lớn hơn 0."; return false; }
            if (vm.Radius <= 0) { error = "Bán kính R phải lớn hơn 0."; return false; }
            if (vm.Branch1CenterRadius - wb1 / 2.0 < -1e-9)
            { error = "Bán kính R quá nhỏ: bán kính cong trong nhỏ hơn 0. Hãy tăng R."; return false; }

            if (vm.NeedsBranch2)
            {
                if (wb2 <= 0) { error = "Đường kính ống rẽ 2 Wb2 phải lớn hơn 0."; return false; }
                if (vm.Radius2 <= 0) { error = "Bán kính R2 phải lớn hơn 0."; return false; }
                if (vm.Branch2CenterRadius - wb2 / 2.0 < -1e-9)
                { error = "Bán kính R2 quá nhỏ: bán kính cong trong nhỏ hơn 0. Hãy tăng R2."; return false; }
            }

            // Mỗi nhánh không được rộng hơn ống chính
            if (wb1 > w + 1e-9) { error = "Wb không được lớn hơn W."; return false; }
            if (vm.NeedsBranch2 && wb2 > w + 1e-9) { error = "Wb2 không được lớn hơn W."; return false; }

            // Ống đi thẳng đi tiếp (eex, eez): độc lập với ống rẽ nhưng không rộng hơn ống chính
            if (vm.NeedsThrough)
            {
                if (vm.ThroughWidth <= 0) { error = "Đường kính ống đi tiếp Ws phải lớn hơn 0."; return false; }
                if (vm.ThroughWidth > w + 1e-9) { error = "Ws không được lớn hơn W."; return false; }
                if (vm.ThroughHeight < 0) { error = "Chiều cao ống đi tiếp Hs không được âm."; return false; }
            }

            return true;
        }

        private static bool AskDouble(Editor ed, string msg, double def, out double value, bool allowZero = false)
        {
            value = def;
            var options = new PromptDoubleOptions("\n" + msg + " <" + def.ToString("0.##") + "> (Enter giữ mặc định): ");
            options.AllowNegative = false;
            options.AllowZero = allowZero;
            options.AllowNone = true;

            PromptDoubleResult result = ed.GetDouble(options);
            if (result.Status == PromptStatus.None) return true;
            if (result.Status != PromptStatus.OK) return false;
            value = result.Value;
            return true;
        }

        private static bool AskKeyword(Editor ed, string msg, string[] keys, string def, out string value)
        {
            value = def;
            var options = new PromptKeywordOptions("\n" + msg + " [" + string.Join("/", keys) + "] <" + def + "> (Enter giữ mặc định): ");
            foreach (string key in keys) options.Keywords.Add(key);
            options.Keywords.Default = def;
            options.AllowNone = true;

            PromptResult result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.None) return true;
            if (result.Status != PromptStatus.OK) return false;
            value = result.StringResult;
            return true;
        }

        private sealed class DuctBranchPreviewJig : DrawJig
        {
            private readonly Point3d _insertPoint;
            private readonly string _commandName;
            private readonly Func<Vector3d, DuctBranchModel> _modelFactory;
            private Point3d _lastPoint;
            private bool _hasLastPoint;

            public DuctBranchPreviewJig(Point3d insertPoint, string commandName,
                Func<Vector3d, DuctBranchModel> modelFactory)
            {
                _insertPoint = insertPoint;
                _commandName = commandName;
                _modelFactory = modelFactory;
            }

            public Vector3d Direction { get; private set; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\n Click để xác nhận hướng ống: ");
                options.BasePoint = _insertPoint;
                options.UseBasePoint = true;
                options.UserInputControls = UserInputControls.Accept3dCoordinates;

                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status != PromptStatus.OK)
                {
                    return SamplerStatus.Cancel;
                }

                if (_hasLastPoint &&
                    result.Value.DistanceTo(_lastPoint) < 1e-8)
                {
                    return SamplerStatus.NoChange;
                }

                _lastPoint = result.Value;
                _hasLastPoint = true;
                Direction = new Vector3d(
                    result.Value.X - _insertPoint.X,
                    result.Value.Y - _insertPoint.Y,
                    0);
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                if (Direction.Length < 1e-8) return true;

                List<Entity> entities = DuctBranchHelper.BuildPreviewEntities(_modelFactory(Direction));
                try
                {
                    foreach (Entity entity in entities)
                        draw.Geometry.Draw(entity);
                }
                finally
                {
                    foreach (Entity entity in entities)
                        entity.Dispose();
                }
                return true;
            }
        }
    }
}