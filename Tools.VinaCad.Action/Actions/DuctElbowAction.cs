using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class DuctElbowAction
    {
        public void Execute(DuctElbowType type)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor editor = doc.Editor;
            Database db = doc.Database;

            DuctElbowVM vm = DuctElbowVM.Get(type);

            double oldW1 = vm.Width1;
            double oldW2 = vm.Width2;
            double oldR = vm.Radius;
            double oldAngle = vm.AngleDeg;
            double oldDrop = vm.DropLength;

            var ppo = new PromptPointOptions("\nChọn điểm chèn: ");
            PromptPointResult promptPointResult = editor.GetPoint(ppo);
            if (promptPointResult.Status != PromptStatus.OK) return;
            Point3d insert = promptPointResult.Value;

            double w1;
            string w1Prompt = type == DuctElbowType.Reducing ? "Nhập kích thước đầu vào W1" : "Nhập kích thước 2 đầu W";
            if (!AskDouble(editor, w1Prompt, vm.Width1, out w1)) return;
            vm.Width1 = w1;

            if (type == DuctElbowType.Reducing)
            {
                double w2;
                if (!AskDouble(editor, "Nhập kích thước đầu ra W2", vm.Width2, out w2)) return;
                vm.Width2 = w2;
            }

            double r;
            if (!AskDouble(editor, "Nhập bán kính đường cong R", vm.SuggestRadius(w1), out r)) return;
            vm.Radius = r;

            if (type == DuctElbowType.Vertical)
            {
                double l;
                if (!AskDouble(editor, "Nhập chiều dài đoạn hạ/ngóc", vm.DropLength, out l)) return;
                vm.DropLength = l;

                string v;
                if (!AskKeyword(editor, "Chọn kiểu", new[] { "Down", "Up" },
                    vm.VerticalDir == VerticalDirection.Down ? "Down" : "Up", out v)) return;
                vm.VerticalDir = v == "Up" ? VerticalDirection.Up : VerticalDirection.Down;
            }
            else
            {
                string s;
                if (!AskKeyword(editor, "Chọn hướng rẽ", new[] { "Left", "Right" },
                        vm.Side == ElbowSide.Left ? "Left" : "Right", out s)) return;
                vm.Side = s == "Right" ? ElbowSide.Right : ElbowSide.Left;

                double a;
                if (!AskDouble(editor, "Nhập góc quay (độ)", vm.AngleDeg, out a)) return;
                vm.AngleDeg = a;
            }

            string err;
            if (!Validate(vm, out err))
            {
                vm.Width1 = oldW1; vm.Width2 = oldW2; vm.Radius = oldR;
                vm.AngleDeg = oldAngle; vm.DropLength = oldDrop;
                editor.WriteMessage("\n" + err);
                return;
            }

            Vector3d direction;
            var jig = new DuctElbowPreviewJig(insert, "DuctElbow", currentDirection =>
            {
                DuctElbowModel previewModel = vm.BuildModel(insert, currentDirection);
                previewModel.ShowLabel = false;
                return previewModel;
            });

            try
            {
                PromptResult result = editor.Drag(jig);
                if (result.Status != PromptStatus.OK) return;
                direction = jig.Direction;
            }
            finally
            {
                jig.Cleanup();  
            }

            if (direction.Length < 1e-6)
            {
                editor.WriteMessage("\nHướng ống không hợp lệ.");
                return;
            }

            DuctElbowModel model = vm.BuildModel(insert, direction);
            model.ShowLabel = false;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DuctElbowHelper.Draw(db, tr, model);
                tr.Commit();
            }
        }

        private static bool Validate(DuctElbowVM vm, out string error)
        {
            error = null;

            if (vm.Width1 <= 0)
            { error = "Kích thước đầu W phải lớn hơn 0."; return false; }

            if (vm.Type == DuctElbowType.Reducing && vm.Width2 <= 0)
            { error = "Kích thước đầu ra W2 phải lớn hơn 0."; return false; }

            if (vm.Radius <= 0)
            { error = "Bán kính R phải lớn hơn 0."; return false; }

            if (vm.Type == DuctElbowType.Vertical)
                return ValidateVertical(vm, out error);

            return ValidateElbow(vm, out error);
        }

        private static bool ValidateElbow(DuctElbowVM vm, out string error)
        {
            error = null;

            if (vm.AngleDeg < DuctElbowSettings.MinAngle || vm.AngleDeg > DuctElbowSettings.MaxAngle)
            {
                error = "Góc quay phải trong khoảng " + DuctElbowSettings.MinAngle + " - " + DuctElbowSettings.MaxAngle + " độ.";
                return false;
            }

            double w2 = vm.Type == DuctElbowType.Reducing ? vm.Width2 : vm.Width1;
            double rc = vm.ResolveCenterRadius(vm.Width1);

            // Bán kính cong trong = Rc - W/2 phải >= 0 (cả đầu vào và đầu ra)
            if (rc - vm.Width1 / 2.0 < -1e-9 || rc - w2 / 2.0 < -1e-9)
            {
                error = "Bán kính R quá nhỏ: bán kính cong trong nhỏ hơn 0. Hãy tăng bán kinh R";
                return false;
            }
            return true;
        }

        private static bool ValidateVertical(DuctElbowVM vm, out string error)
        {
            error = null;

            if (vm.DropLength <= 0)
            { error = "Chiều dài đoạn hạ phải lớn hơn 0."; return false; }

            if (vm.ResolveCenterRadius(vm.Width1) < vm.Width1 / 2.0)
            { error = "Bán kính R phải lớn hơn hoặc bằng W/2."; return false; }

            return true;
        }

        private static bool AskDouble(Editor ed, string msg, double def, out double value)
        {
            value = def;

            string defaultText = def.ToString("0.##", CultureInfo.CurrentCulture);

            var options = new PromptStringOptions("\n" + msg + ": ")
            {
                AllowSpaces = false,
                DefaultValue = defaultText
            };

            PromptResult result = ed.GetString(options);

            if (result.Status == PromptStatus.None)
            {
                value = def;
                return true;
            }

            if (result.Status != PromptStatus.OK)
                return false;

            string input = result.StringResult;

            if (string.IsNullOrWhiteSpace(input))
            {
                value = def;
                return true;
            }

            if (double.TryParse(input,NumberStyles.Float,CultureInfo.CurrentCulture,out double parsedValue)
                || 
                double.TryParse(input,NumberStyles.Float,CultureInfo.InvariantCulture,out parsedValue))
            {
                if (parsedValue <= 0)
                {
                    ed.WriteMessage("\nGiá trị phải lớn hơn 0.");
                    return false;
                }

                value = parsedValue;
                return true;
            }

            ed.WriteMessage("\nGiá trị nhập vào không hợp lệ.");
            return false;
        }

        private static bool AskKeyword(Editor ed, string msg, string[] keys, string def, out string value)
        {
            value = def;
            var options = new PromptKeywordOptions("\n" + msg + " [" + string.Join("/", keys) + "] <" + def + "> (Enter giữ mặc định): ");

            foreach (string key in keys)
                options.Keywords.Add(key);

            options.Keywords.Default = def;
            options.AllowNone = true;

            PromptResult result = ed.GetKeywords(options);

            if (result.Status == PromptStatus.None)
                return true;

            if (result.Status != PromptStatus.OK)
                return false;

            value = result.StringResult;
            return true;
        }


        private sealed class DuctElbowPreviewJig : DrawJig
        {
            private readonly Point3d _insertPoint;
            private readonly string _commandName;
            private readonly Func<Vector3d, DuctElbowModel> _modelFactory;

            private Point3d _lastPoint;
            private bool _hasLastPoint;

            public DuctElbowPreviewJig(Point3d insertPoint,string commandName,Func<Vector3d, DuctElbowModel> modelFactory)
            {
                _insertPoint = insertPoint;
                _commandName = commandName;
                _modelFactory = modelFactory;
            }

            public Vector3d Direction { get; private set; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\n[" + _commandName + "] Click chuột để xác nhận hướng ống: ");

                options.BasePoint = _insertPoint;
                options.UseBasePoint = true;
                options.UserInputControls = UserInputControls.Accept3dCoordinates;

                PromptPointResult result = prompts.AcquirePoint(options);

                if (result.Status == PromptStatus.Cancel)
                    return SamplerStatus.Cancel;

                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.Cancel;

                if (_hasLastPoint && result.Value.DistanceTo(_lastPoint) < 1e-8)
                    return SamplerStatus.NoChange;

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
                if (Direction.Length < 1e-8)
                    return true;

                DuctElbowModel model = _modelFactory(Direction);
                System.Collections.Generic.List<Entity> entities =
                    DuctElbowHelper.BuildPreviewEntities(model);

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

            public void Cleanup()
            {
                ClearPreview();
            }

            private void ClearPreview()
            {
            }
        }
    }
}