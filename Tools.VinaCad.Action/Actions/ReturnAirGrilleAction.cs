using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Diagnostics;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using CadColor = Teigha.Colors.Color;
using ColorMethod = Teigha.Colors.ColorMethod;
using WorldDraw = Teigha.GraphicsInterface.WorldDraw;

namespace Tools.VinaCAD.Action.Actions
{
    public class ReturnAirGrilleAction
    {
        public void Execute(bool eggCrate)
        {
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;

                Editor editor = document.Editor;
                if (!AskPositive(editor, "Nhập chiều rộng khung trong miệng gió", ReturnAirGrilleSetting.DefaultWidth, out double width)) return;
                if (!AskPositive(editor, "Nhập chiều cao khung trong miệng gió", ReturnAirGrilleSetting.DefaultHeight, out double height)) return;
                Debug.Assert(IsValidSize(width, height));

                Database database = document.Database;
                string baseName = (eggCrate ? "GrillV" : "Grill") + '_' + FormatSize(width) + "x" + FormatSize(height);
                string defaultName = GetAvailableName(database, baseName);
                PromptResult nameResult = editor.GetString(new PromptStringOptions("\nNhập tên block miệng gió : ")
                {
                    AllowSpaces = false,
                    DefaultValue = defaultName,
                    UseDefaultValue = true
                });
                if (nameResult.Status != PromptStatus.OK && nameResult.Status != PromptStatus.None) return;

                string name = nameResult.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(nameResult.StringResult)
                    ? nameResult.StringResult.Trim()
                    : defaultName;
                if (BlockExists(database, name))
                {
                    editor.WriteMessage("\nTên block đã tồn tại. Hãy nhập tên khác.");
                    return;
                }

                ObjectId blockId = ObjectId.Null;
                while (true)
                {
                    var jig = new GrillePlacementJig(width, height, eggCrate, editor.CurrentUserCoordinateSystem);
                    if (editor.Drag(jig).Status != PromptStatus.OK) break;

                    using (Transaction tr = database.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord space = (BlockTableRecord)tr.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                        if (blockId.IsNull)
                        {
                            BlockTable blocks = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForWrite);
                            var definition = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
                            blockId = blocks.Add(definition);
                            tr.AddNewlyCreatedDBObject(definition, true);
                            AddGeometry(definition, tr, width, height, eggCrate);
                        }

                        var reference = new BlockReference(jig.InsertionPoint, blockId);
                        reference.SetDatabaseDefaults(database);
                        reference.LayerId = GetorCreateLayer(database, tr, ReturnAirGrilleSetting.DefaultLayer);
                        reference.TransformBy(jig.Ucs);
                        space.AppendEntity(reference);
                        tr.AddNewlyCreatedDBObject(reference, true);
                        tr.Commit();
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(ReturnAirGrilleAction), ex);
            }
        }

        private static bool IsValidSize(double width, double height)
        {
            bool valid = double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0;
            Debug.Assert(!valid ||
                width / (GetInteriorLineCount(width) + 1) <= ReturnAirGrilleSetting.DefaultBladeSpacing &&
                height / (GetInteriorLineCount(height) + 1) <= ReturnAirGrilleSetting.DefaultBladeSpacing);
            return valid;
        }

        private static string FormatSize(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static bool AskPositive(Editor editor, string label, double defaultValue, out double value)
        {
            PromptStringOptions options = new PromptStringOptions($"\n{label} : ")
            {
                AllowSpaces = false,
                DefaultValue = defaultValue.ToString("0.##", CultureInfo.CurrentCulture),
                UseDefaultValue = true
            };
            PromptResult result = editor.GetString(options);
            value = defaultValue;
            if (result.Status != PromptStatus.OK && result.Status != PromptStatus.None) return false;

            if (result.Status == PromptStatus.OK &&
                !string.IsNullOrWhiteSpace(result.StringResult) &&
                !double.TryParse(result.StringResult, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
                !double.TryParse(result.StringResult, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                editor.WriteMessage($"\n{label} phải là số lớn hơn 0.");
                return false;
            }
            if (!double.IsFinite(value) || value <= 0)
            {
                editor.WriteMessage($"\n{label} phải là số lớn hơn 0.");
                return false;
            }
            return true;
        }

        private static string GetAvailableName(Database database, string baseName)
        {
            string name = baseName;
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                BlockTable blocks = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
                for (int suffix = 1; blocks.Has(name); suffix++)
                {
                    name = baseName + "_" + suffix;
                }
            }
            return name;
        }

        private static bool BlockExists(Database database, string name)
        {
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                BlockTable blocks = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
                return blocks.Has(name);
            }
        }

        private static void AddGeometry(BlockTableRecord definition, Transaction tr, double width, double height, bool eggCrate)
        {
            double halfWidth = width / 2;
            double halfHeight = height / 2;
            double frame = Math.Min(width, height) * 0.05;
            double axisMargin = Math.Min(width, height) * 0.25;

            AddRectangle(definition, tr, halfWidth + frame, halfHeight + frame, 8, LineWeight.ByLineWeightDefault);
            AddRectangle(definition, tr, halfWidth, halfHeight, 7, LineWeight.LineWeight050);

            AddInteriorLines(definition, tr, -halfHeight, halfHeight, halfWidth, false);
            if (eggCrate) AddInteriorLines(definition, tr, -halfWidth, halfWidth, halfHeight, true);

            AddLine(definition, tr,
                new Point3d(-halfWidth - frame - axisMargin, 0, 0),
                new Point3d(halfWidth + frame + axisMargin, 0, 0), 8);
            AddLine(definition, tr,
                new Point3d(0, -halfHeight - frame - axisMargin, 0),
                new Point3d(0, halfHeight + frame + axisMargin, 0), 8);
        }

        private static void AddInteriorLines(BlockTableRecord definition,Transaction tr,double start,double end,double halfOther,bool vertical)
        {
            double halfLength = (end - start) / 2;

            for (double offset = ReturnAirGrilleSetting.DefaultBladeSpacing;
                 offset < halfLength;
                 offset += ReturnAirGrilleSetting.DefaultBladeSpacing)
            {
                if (vertical)
                {
                    AddLine(definition, tr,
                        new Point3d(-offset, -halfOther, 0),
                        new Point3d(-offset, halfOther, 0), 8);

                    AddLine(definition, tr,
                        new Point3d(offset, -halfOther, 0),
                        new Point3d(offset, halfOther, 0), 8);
                }
                else
                {
                    AddLine(definition, tr,
                        new Point3d(-halfOther, -offset, 0),
                        new Point3d(halfOther, -offset, 0), 8);

                    AddLine(definition, tr,
                        new Point3d(-halfOther, offset, 0),
                        new Point3d(halfOther, offset, 0), 8);
                }
            }
        }

        private static int GetInteriorLineCount(double length) =>
            Math.Max(0, (int)Math.Ceiling(length / ReturnAirGrilleSetting.DefaultBladeSpacing) - 1);

        private static void AddRectangle(
            BlockTableRecord definition, Transaction tr, double halfWidth, double halfHeight,
            short colorIndex, LineWeight lineWeight)
        {
            var rectangle = new Polyline
            {
                Closed = true,
                Color = CadColor.FromColorIndex(ColorMethod.ByAci, colorIndex),
                LineWeight = lineWeight
            };
            rectangle.AddVertexAt(0, new Point2d(-halfWidth, -halfHeight), 0, 0, 0);
            rectangle.AddVertexAt(1, new Point2d(halfWidth, -halfHeight), 0, 0, 0);
            rectangle.AddVertexAt(2, new Point2d(halfWidth, halfHeight), 0, 0, 0);
            rectangle.AddVertexAt(3, new Point2d(-halfWidth, halfHeight), 0, 0, 0);
            definition.AppendEntity(rectangle);
            tr.AddNewlyCreatedDBObject(rectangle, true);
        }

        private static void AddLine(BlockTableRecord definition, Transaction tr, Point3d start, Point3d end, short colorIndex)
        {
            var line = new Line(start, end)
            {
                Color = CadColor.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
            definition.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private sealed class GrillePlacementJig : DrawJig
        {
            private readonly double _width;
            private readonly double _height;
            private readonly bool _eggCrate;
            private Point3d _insertionPoint;
            private bool _hasPoint;

            public Point3d InsertionPoint => _insertionPoint;
            public Matrix3d Ucs { get; }

            public GrillePlacementJig(double width, double height, bool eggCrate, Matrix3d ucs)
            {
                _width = width;
                _height = height;
                _eggCrate = eggCrate;
                Ucs = ucs;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\nChọn điểm đặt tâm miệng gió : ")
                {
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };
                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                if (_hasPoint && result.Value == _insertionPoint) return SamplerStatus.NoChange;
                _insertionPoint = result.Value;
                _hasPoint = true;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                double halfWidth = _width / 2;
                double halfHeight = _height / 2;
                double frame = Math.Min(_width, _height) * 0.05;
                double axisMargin = Math.Min(_width, _height) * 0.25;

                draw.SubEntityTraits.Color = 8;
                draw.SubEntityTraits.LineWeight = LineWeight.ByLineWeightDefault;
                DrawRectangle(draw, halfWidth + frame, halfHeight + frame);
                draw.SubEntityTraits.Color = 7;
                draw.SubEntityTraits.LineWeight = LineWeight.LineWeight050;
                DrawRectangle(draw, halfWidth, halfHeight);
                draw.SubEntityTraits.Color = 8;
                draw.SubEntityTraits.LineWeight = LineWeight.ByLineWeightDefault;
                DrawInteriorLines(draw, -halfHeight, halfHeight, halfWidth, false);
                if (_eggCrate) DrawInteriorLines(draw, -halfWidth, halfWidth, halfHeight, true);
                DrawLine(draw, -halfWidth - frame - axisMargin, 0, halfWidth + frame + axisMargin, 0);
                DrawLine(draw, 0, -halfHeight - frame - axisMargin, 0, halfHeight + frame + axisMargin);
                return true;
            }

            private void DrawRectangle(WorldDraw draw, double halfWidth, double halfHeight)
            {
                DrawLine(draw, -halfWidth, -halfHeight, halfWidth, -halfHeight);
                DrawLine(draw, halfWidth, -halfHeight, halfWidth, halfHeight);
                DrawLine(draw, halfWidth, halfHeight, -halfWidth, halfHeight);
                DrawLine(draw, -halfWidth, halfHeight, -halfWidth, -halfHeight);
            }

            private void DrawInteriorLines(WorldDraw draw,double start,double end,double halfOther,bool vertical)
            {
                double halfLength = (end - start) / 2;

                for (double offset = ReturnAirGrilleSetting.DefaultBladeSpacing;
                     offset < halfLength;
                     offset += ReturnAirGrilleSetting.DefaultBladeSpacing)
                {
                    if (vertical)
                    {
                        DrawLine(draw, -offset, -halfOther, -offset, halfOther);
                        DrawLine(draw, offset, -halfOther, offset, halfOther);
                    }
                    else
                    {
                        DrawLine(draw, -halfOther, -offset, halfOther, -offset);
                        DrawLine(draw, -halfOther, offset, halfOther, offset);
                    }
                }
            }

            private void DrawLine(WorldDraw draw, double x1, double y1, double x2, double y2)
            {
                Point3d start = new Point3d(
                    _insertionPoint.X + x1, _insertionPoint.Y + y1, _insertionPoint.Z).TransformBy(Ucs);
                Point3d end = new Point3d(
                    _insertionPoint.X + x2, _insertionPoint.Y + y2, _insertionPoint.Z).TransformBy(Ucs);
                draw.Geometry.WorldLine(start, end);
            }
        }

        private static ObjectId GetorCreateLayer(Database database, Transaction tr, string layerName)
        {
            LayerTable layers = (LayerTable)tr.GetObject(database.LayerTableId, OpenMode.ForRead);

            if (layers.Has(layerName))
            {
                return layers[layerName];
            }

            layers.UpgradeOpen();  

            var layer = new LayerTableRecord
            {
                Name = ReturnAirGrilleSetting.DefaultLayer,
                Color = CadColor.FromColorIndex(ColorMethod.ByAci, 7) 
            };

            ObjectId layerId = layers.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);

            return layerId;
        }
    }
}
