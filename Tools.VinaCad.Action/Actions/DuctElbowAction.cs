using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.Model;
using Tools.Resources.Language;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Tools.VinaCAD.Action.Actions;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DuctElbowAction
    {
        public void Execute(DuctElbowType type, bool threeDimensional = false)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Editor editor = doc.Editor;
                Database db = doc.Database;

                DuctElbowVM vm = DuctElbowVM.Get(type);

                double oldW1 = vm.Width1;
                double oldW2 = vm.Width2;
                double oldR = vm.Radius;
                double oldAngle = vm.AngleDeg;
                double oldHeight = vm.Height;
                double oldDrop = vm.DropLength;

                var ppo = new PromptPointOptions("\n" + Lang.DuctElbow_Prompt_ChonDiemChen);
                PromptPointResult promptPointResult = editor.GetPoint(ppo);
                if (promptPointResult.Status != PromptStatus.OK) return;
                Point3d insert = promptPointResult.Value;

                double w1;
                string w1Prompt = type == DuctElbowType.Reducing ? Lang.DuctElbow_Prompt_KichThuocDauVao : Lang.DuctElbow_Prompt_KichThuocHaiDau;
                if (!AskDouble(editor, w1Prompt, vm.Width1, out w1)) return;
                vm.Width1 = w1;

                if (type == DuctElbowType.Reducing)
                {
                    double w2;
                    if (!AskDouble(editor, Lang.DuctElbow_Prompt_KichThuocDauRa, vm.Width2, out w2)) return;
                    vm.Width2 = w2;
                }

                if (threeDimensional)
                {
                    if (!AskDouble(editor, Lang.DuctElbow_Prompt_ChieuCaoOng, vm.Height, out double height)) return;
                    vm.Height = height;
                }

                double r;

                if (!AskDouble(editor, Lang.DuctElbow_Prompt_BanKinhCongTrong, vm.Radius, out r))
                {
                    return;
                }

                vm.Radius = r;

                if (type == DuctElbowType.Vertical)
                {
                    if (threeDimensional)
                    {
                        if (!AskDouble(editor, Lang.DuctElbow_Prompt_ChieuDaiDoanDung, vm.DropLength, out double dropLength)) return;
                        vm.DropLength = dropLength;
                    }

                    string v;
                    if (!AskKeyword(editor, Lang.DuctElbow_Prompt_ChonHuong,
                        "Down", Lang.DuctElbow_Keyword_Xuong, "Up", Lang.DuctElbow_Keyword_Len,
                        vm.VerticalDir == VerticalDirection.Down ? "Down" : "Up", out v)) return;
                    vm.VerticalDir = v == "Up" || v == Lang.DuctElbow_Keyword_Len ? VerticalDirection.Up : VerticalDirection.Down;
                }
                else
                {
                    string s;
                    if (!AskKeyword(editor, Lang.DuctElbow_Prompt_ChonHuongRe,
                            "Left", Lang.DuctElbow_Keyword_Trai, "Right", Lang.DuctElbow_Keyword_Phai,
                            vm.Side == ElbowSide.Left ? "Left" : "Right", out s)) return;
                    vm.Side = s == "Right" || s == Lang.DuctElbow_Keyword_Phai ? ElbowSide.Right : ElbowSide.Left;

                    double a;
                    if (!AskDouble(editor, Lang.DuctElbow_Prompt_GocQuay, vm.AngleDeg, out a)) return;
                    vm.AngleDeg = a;
                }

                string err;
                if (!Validate(vm, threeDimensional, out err))
                {
                    vm.Width1 = oldW1;
                    vm.Width2 = oldW2;
                    vm.Radius = oldR;
                    vm.AngleDeg = oldAngle;
                    vm.Height = oldHeight;
                    vm.DropLength = oldDrop;
                    editor.WriteMessage("\n" + err);
                    return;
                }

                Vector3d direction;
                var jig = new DuctElbowPreviewJig(insert, "DuctElbow", currentDirection =>
                {
                    DuctElbowModel previewModel = vm.BuildModel(insert, currentDirection);
                    previewModel.ShowLabel = false;
                    return previewModel;
                }, threeDimensional);

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
                    editor.WriteMessage("\n" + Lang.DuctElbow_Output_HuongOngKhongHopLe);
                    return;
                }

                DuctElbowModel model = vm.BuildModel(insert, direction);
                model.ShowLabel = false;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (threeDimensional)
                        DuctElbowHelper.Draw3D(db, tr, model);
                    else
                        DuctElbowHelper.Draw(db, tr, model);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Logger.Info(nameof(DuctElbowAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private static bool Validate(DuctElbowVM vm, bool threeDimensional, out string error)
        {
            error = null;

            if (threeDimensional && vm.Height <= 0)
            { error = Lang.DuctElbow_Validation_ChieuCaoOng; return false; }

            if (vm.Width1 <= 0)
            { error = Lang.DuctElbow_Validation_KichThuocDauVao; return false; }

            if (vm.Type == DuctElbowType.Reducing && vm.Width2 <= 0)
            { error = Lang.DuctElbow_Validation_KichThuocDauRa; return false; }

            if (vm.Radius <= 0)
            { error = Lang.DuctElbow_Validation_BanKinhCongTrong; return false; }

            if (vm.Type == DuctElbowType.Vertical)
                return ValidateVertical(vm, threeDimensional, out error);

            return ValidateElbow(vm, out error);
        }

        private static bool ValidateElbow(DuctElbowVM vm, out string error)
        {
            error = null;

            if (vm.AngleDeg < DuctElbowSettings.MinAngle || vm.AngleDeg > DuctElbowSettings.MaxAngle)
            {
                error = Lang.DuctElbow_Validation_GocQuay
                    .Replace("{minAngle}", DuctElbowSettings.MinAngle.ToString(CultureInfo.CurrentCulture))
                    .Replace("{maxAngle}", DuctElbowSettings.MaxAngle.ToString(CultureInfo.CurrentCulture));
                return false;
            }

            double w2 = vm.Type == DuctElbowType.Reducing ? vm.Width2 : vm.Width1;
            double rc = vm.ResolveCenterRadius(vm.Width1);

            // Bán kính cong trong = Rc - W/2 phải >= 0 (cả đầu vào và đầu ra)
            if (rc - vm.Width1 / 2.0 < -1e-9 || rc - w2 / 2.0 < -1e-9)
            {
                error = Lang.DuctElbow_Validation_BanKinhQuaNho;
                return false;
            }
            return true;
        }

        private static bool ValidateVertical(DuctElbowVM vm, bool threeDimensional, out string error)
        {
            error = null;

            if (vm.DropLength <= 0)
            { error = Lang.DuctElbow_Validation_ChieuDaiDoanHa; return false; }

            if (vm.ResolveCenterRadius(vm.Width1) < vm.Width1 / 2.0)
            { error = Lang.DuctElbow_Validation_BanKinhNuaDuongKinh; return false; }

            if (threeDimensional && vm.ResolveCenterRadius(vm.Width1) <= vm.Width1 / 2.0)
            { error = Lang.DuctElbow_Validation_BanKinhCongTrong3D; return false; }

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
                    ed.WriteMessage("\n" + Lang.DuctElbow_Output_GiaTriPhaiLonHon0);
                    return false;
                }

                value = parsedValue;
                return true;
            }

            ed.WriteMessage("\n" + Lang.DuctElbow_Output_GiaTriNhapKhongHopLe);
            return false;
        }

        private static bool AskKeyword(Editor ed, string msg,
            string firstGlobal, string firstLocal, string secondGlobal, string secondLocal,
            string defaultGlobal, out string value)
        {
            value = defaultGlobal;
            string defaultLocal = defaultGlobal == firstGlobal ? firstLocal : secondLocal;
            var options = new PromptKeywordOptions("\n" + msg + " [" + firstLocal + "/" + secondLocal + "] <" + defaultLocal + ">: ")
            {
                AppendKeywordsToMessage = false
            };
            options.Keywords.Add(firstGlobal, firstLocal, firstLocal);
            options.Keywords.Add(secondGlobal, secondLocal, secondLocal);
            options.Keywords.Default = defaultGlobal;
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
            private readonly bool _threeDimensional;

            private Point3d _lastPoint;
            private bool _hasLastPoint;

            public DuctElbowPreviewJig(Point3d insertPoint,string commandName,Func<Vector3d, DuctElbowModel> modelFactory, bool threeDimensional)
            {
                _insertPoint = insertPoint;
                _commandName = commandName;
                _modelFactory = modelFactory;
                _threeDimensional = threeDimensional;
            }

            public Vector3d Direction { get; private set; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\n" + Lang.DuctElbow_Prompt_XacNhanHuongOng);

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
                    _threeDimensional
                        ? new System.Collections.Generic.List<Entity> { DuctElbowHelper.BuildSolid(model) }
                        : DuctElbowHelper.BuildPreviewEntities(model);

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