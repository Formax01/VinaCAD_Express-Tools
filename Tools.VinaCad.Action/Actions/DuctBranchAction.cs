using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.GraphicsInterface;
using Teigha.Geometry;
using Tools.Model;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using Polyline = Teigha.DatabaseServices.Polyline;

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

                // 1) Điểm đặt
                PromptPointResult point = editor.GetPoint(new PromptPointOptions("\nChọn điểm đặt: "));
                if (point.Status != PromptStatus.OK) return;

                // 2) Nhập số. Sau khi có W, mọi giá trị khác được đặt sẵn theo dáng mặc định rồi mới hỏi.
                var vm = new DuctBranchVM(type);
                if (!AskInputs(editor, vm)) return;

                string error = Validate(vm);
                if (error != null)
                {
                    editor.WriteMessage("\n" + error);
                    return;
                }

                // 3) Chọn hướng đặt: rê chuột xem trước, click để chốt
                Point3d insert = point.Value;
                var jig = new PreviewJig(insert, direction => vm.ToModel(insert, direction));
                if (editor.Drag(jig).Status != PromptStatus.OK) return;

                if (jig.Direction.Length < Eps)
                {
                    editor.WriteMessage("\nHướng ống không hợp lệ.");
                    return;
                }

                // 4) Vẽ
                DuctBranchModel model = vm.ToModel(insert, jig.Direction);
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    DuctBranchHelper.Draw(doc.Database, tr, model);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLỗi: " + ex.Message);
            }
        }

        private const double Eps = 1e-6;

        private static bool AskInputs(Editor editor, DuctBranchVM vm)
        {
            if (!AskNumber(editor, "Nhập đường kính ống chính ", ref vm.MainWidth)) return false;

            if (!AskNumber(editor, "Nhập chiều cao ống chính (đoạn thẳng trước chỗ rẽ)", ref vm.MainHeight, true)) return false;

            if (vm.HasThrough)
            {
                if (!AskNumber(editor, "Nhập đường kính ống đi thẳng đi tiếp ", ref vm.ThroughWidth)) return false;
                if (!AskNumber(editor, "Nhập chiều cao ống đi tiếp (đoạn thẳng vượt qua miệng nhánh)", ref vm.ThroughHeight, true)) return false;
            }

            // Nhánh 1 (eez: nhánh trái; eex, eey: nhánh duy nhất / cả hai nhánh)
            string n1 = vm.HasBranch2 ? "1" : "";
            if (!AskNumber(editor, "Nhập đường kính ống rẽ Wb" + n1, ref vm.BranchWidth)) return false;
            if (!AskNumber(editor, "Nhập bán kính cong nhánh rẽ R" + n1, ref vm.Radius)) return false;

            // Nhánh 2 (chỉ eez)
            if (vm.HasBranch2)
            {
                if (!AskNumber(editor, "Nhập đường kính ống rẽ Wb2", ref vm.Branch2Width)) return false;
                if (!AskNumber(editor, "Nhập bán kính cong nhánh rẽ R2", ref vm.Radius2)) return false;
            }
            if (!AskNumber(editor, "Nhập chiều cao ống rẽ Hb (đoạn thẳng sau cung rẽ)", ref vm.BranchHeight, true)) return false;

            // Hướng rẽ (chỉ eex)
            if (vm.HasSide)
            {
                string side = vm.Side == ElbowSide.Left ? "Left" : "Right";
                if (!AskSide(editor, ref side)) return false;
                vm.Side = side == "Right" ? ElbowSide.Right : ElbowSide.Left;
            }
            return true;
        }

        public string Validate(DuctBranchVM vm)
        {
            if (vm.MainWidth <= 0) return "W phải lớn hơn 0.";
            if (vm.MainHeight < 0) return "H không được âm.";
            if (vm.BranchHeight < 0) return "Hb không được âm.";
            string error = CheckBranch(vm, "", vm.BranchWidth, vm.Radius);
            if (error != null) return error;

            if (vm.HasBranch2)
            {
                error = CheckBranch(vm, "2", vm.Branch2Width, vm.Radius2);
                if (error != null) return error;
            }

            if (vm.HasThrough)
            {
                if (vm.ThroughWidth <= 0) return "Ws phải lớn hơn 0.";
                if (vm.ThroughWidth > vm.MainWidth) return "Ws không được lớn hơn W.";
                if (vm.ThroughHeight < 0) return "Hs không được âm.";
            }
            return null;
        }

        private static string CheckBranch(DuctBranchVM vm, string name, double width, double radius)
        {
            if (width <= 0) return "Wb" + name + " phải lớn hơn 0.";
            if (width > vm.MainWidth) return "Wb" + name + " không được lớn hơn W.";
            if (radius - width / 2 < 0) return "R" + name + " quá nhỏ (bán kính cong trong âm), hãy tăng R" + name + ".";
            return null;
        }

        private static bool AskNumber(Editor editor, string message, ref double value, bool allowZero = false)
        {
            string defaultText = value.ToString("0.##", CultureInfo.CurrentCulture);
            var options = new PromptStringOptions("\n" + message + ": ")
            {
                AllowSpaces = false,
                DefaultValue = defaultText
            };

            PromptResult result = editor.GetString(options);
            if (result.Status == PromptStatus.None) return true;
            if (result.Status != PromptStatus.OK) return false;

            if ((!double.TryParse(result.StringResult, NumberStyles.Float, CultureInfo.CurrentCulture, out double input) &&
                 !double.TryParse(result.StringResult, NumberStyles.Float, CultureInfo.InvariantCulture, out input)) ||
                double.IsNaN(input) || double.IsInfinity(input) || input < 0 || (!allowZero && input == 0))
            {
                editor.WriteMessage("\nGiá trị phải là số " + (allowZero ? "không âm." : "lớn hơn 0."));
                return false;
            }

            value = input;
            return true;
        }

        private static bool AskSide(Editor editor, ref string side)
        {
            var options = new PromptKeywordOptions("\nChọn hướng rẽ [Trái/Phải] <" + (side == "Left" ? "Trái" : "Phải") + ">: ")
            {
                AllowNone = true,
                AppendKeywordsToMessage = false
            };
            options.Keywords.Add("Left", "Trái", "Trái");
            options.Keywords.Add("Right", "Phải", "Phải");
            options.Keywords.Default = side;

            PromptResult result = editor.GetKeywords(options);
            if (result.Status == PromptStatus.None) return true;
            if (result.Status != PromptStatus.OK) return false;

            side = result.StringResult;
            return true;
        }

        private sealed class PreviewJig : DrawJig
        {
            private readonly Point3d _insertPoint;
            private readonly Func<Vector3d, DuctBranchModel> _createModel;
            private Point3d _lastPoint;
            private bool _hasLastPoint;

            public PreviewJig(Point3d insertPoint, Func<Vector3d, DuctBranchModel> createModel)
            {
                _insertPoint = insertPoint;
                _createModel = createModel;
            }

            public Vector3d Direction { get; private set; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\n Click để xác nhận hướng ống: ");
                options.BasePoint = _insertPoint;
                options.UseBasePoint = true;
                options.UserInputControls = UserInputControls.Accept3dCoordinates;

                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;

                // Chuột đứng yên thì không vẽ lại
                if (_hasLastPoint && result.Value.DistanceTo(_lastPoint) < 1e-8) return SamplerStatus.NoChange;

                _lastPoint = result.Value;
                _hasLastPoint = true;
                Direction = new Vector3d(result.Value.X - _insertPoint.X, result.Value.Y - _insertPoint.Y, 0);
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                if (Direction.Length < 1e-8) return true;

                Polyline outline = DuctBranchHelper.CreateOutline(_createModel(Direction));
                try
                {
                    draw.Geometry.Draw(outline);
                }
                finally
                {
                    outline.Dispose();
                }
                return true;
            }
        }
    }
}