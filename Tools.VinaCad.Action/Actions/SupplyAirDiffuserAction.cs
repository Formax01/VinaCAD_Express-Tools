using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using WorldDraw = Teigha.GraphicsInterface.WorldDraw;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using Region = Teigha.DatabaseServices.Region;

namespace Tools.VinaCAD.Action.Actions
{
    public class SupplyAirDiffuserAction
    {
        public void Execute()
        {
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;
                Editor editor = document.Editor;

                if (!AskPositive(editor, "Nhập độ rộng miệng gió cấp",SupplyAirDiffuserSetting.DefaultSupplyAirWidth, out double width)) return;

                while (true)
                {
                    PromptPointResult result = editor.GetPoint("\nChọn vị trí đặt miệng gió cấp : ");
                    if (result.Status != PromptStatus.OK) break;

                    Point3d center = result.Value;
                    Matrix3d ucs = editor.CurrentUserCoordinateSystem;
                    Database database = document.Database;

                    using (Transaction tr = database.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord space = (BlockTableRecord)tr.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                        ObjectIdCollection ids = new ObjectIdCollection();

                        double inner = width / 2;
                        double outer = width * 0.75;

                        AddSquare(space, tr, ids, center, inner, center.Z, ucs);
                        AddSquare(space, tr, ids, center, outer, center.Z, ucs);
                        AddCornerLines(space, tr, ids, center, inner, center.Z, outer, center.Z, ucs);

                        double axisHalf = width;
                        Line ax1 = AddLine(space, tr, ids,
                            new Point3d(center.X - axisHalf, center.Y, center.Z),
                            new Point3d(center.X + axisHalf, center.Y, center.Z), ucs);
                        Line ax2 = AddLine(space, tr, ids,
                            new Point3d(center.X, center.Y - axisHalf, center.Z),
                            new Point3d(center.X, center.Y + axisHalf, center.Z), ucs);
                        ax1.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 8);
                        ax2.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 8);

                        CreateGroup(database, tr, ids, "SupAir");
                        tr.Commit();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Info(nameof(SupplyAirDiffuserAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        public void ExecuteBlock()
        {
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;
                Editor editor = document.Editor;
                if (!AskPositive(editor, "Nhập độ rộng miệng gió cấp", SupplyAirDiffuserSetting.DefaultSupplyAirWidth, out double width)) return;

                Database database = document.Database;
                string baseName = "SupAirDiff_" + width.ToString("0.##", CultureInfo.InvariantCulture);
                string defaultName = baseName;
                using (Transaction tr = database.TransactionManager.StartTransaction())
                {
                    BlockTable blocks = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
                    for (int suffix = 2; blocks.Has(defaultName); suffix++)
                        defaultName = baseName + "_" + suffix;
                    System.Diagnostics.Debug.Assert(!blocks.Has(defaultName));
                }

                PromptResult nameResult = editor.GetString(new PromptStringOptions($"\nNhập tên block miệng gió cấp : ")
                {
                    AllowSpaces = false,
                    DefaultValue = defaultName,
                    UseDefaultValue = true
                });
                if (nameResult.Status != PromptStatus.OK && nameResult.Status != PromptStatus.None) return;
                string name = nameResult.Status == PromptStatus.OK &&
                    !string.IsNullOrWhiteSpace(nameResult.StringResult)
                    ? nameResult.StringResult.Trim() : defaultName;

                using (Transaction tr = database.TransactionManager.StartTransaction())
                {
                    BlockTable blocks = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
                    if (blocks.Has(name))
                    {
                        editor.WriteMessage("\nTên block đã tồn tại. Hãy nhập tên khác.");
                        return;
                    }
                }

                ObjectId blockId = ObjectId.Null;
                while (true)
                {
                    var jig = new DiffuserPlacementJig(width, editor.CurrentUserCoordinateSystem);
                    PromptResult placement = editor.Drag(jig);
                    if (placement.Status != PromptStatus.OK) break;

                    using (Transaction tr = database.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord space = (BlockTableRecord)tr.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                        if (blockId.IsNull)
                        {
                            BlockTable blocks = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForWrite);
                            var definition = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
                            blockId = blocks.Add(definition);
                            tr.AddNewlyCreatedDBObject(definition, true);

                            var ids = new ObjectIdCollection();
                            double inner = width / 2;
                            double outer = width * 0.75;
                            AddSquare(definition, tr, ids, Point3d.Origin, inner, 0, Matrix3d.Identity);
                            AddSquare(definition, tr, ids, Point3d.Origin, outer, 0, Matrix3d.Identity);
                            AddCornerLines(definition, tr, ids, Point3d.Origin, inner, 0, outer, 0, Matrix3d.Identity);
                            Line ax1 = AddLine(definition, tr, ids,
                                new Point3d(-width, 0, 0), new Point3d(width, 0, 0), Matrix3d.Identity);
                            Line ax2 = AddLine(definition, tr, ids,
                                new Point3d(0, -width, 0), new Point3d(0, width, 0), Matrix3d.Identity);
                            ax1.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 8);
                            ax2.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 8);
                        }

                        var reference = new BlockReference(jig.Center, blockId);
                        reference.SetDatabaseDefaults(database);
                        reference.TransformBy(jig.Ucs);
                        space.AppendEntity(reference);
                        tr.AddNewlyCreatedDBObject(reference, true);
                        tr.Commit();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Info(nameof(SupplyAirDiffuserAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private sealed class DiffuserPlacementJig : DrawJig
        {
            private readonly double _width;
            private Point3d _center;
            private bool _hasCenter;
            public Point3d Center => _center;
            public Matrix3d Ucs { get; }

            public DiffuserPlacementJig(double width, Matrix3d ucs)
            {
                _width = width;
                Ucs = ucs;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\nChọn vị trí đặt miệng gió cấp : ")
                {
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };
                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                if (_hasCenter && result.Value == _center) return SamplerStatus.NoChange;
                _center = result.Value;
                _hasCenter = true;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                double inner = _width / 2;
                double outer = _width * 0.75;
                int[] sx = { -1, 1, 1, -1 };
                int[] sy = { -1, -1, 1, 1 };
                draw.SubEntityTraits.Color = 7;
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    DrawLine(draw, sx[i] * inner, sy[i] * inner, sx[next] * inner, sy[next] * inner);
                    DrawLine(draw, sx[i] * outer, sy[i] * outer, sx[next] * outer, sy[next] * outer);
                    DrawLine(draw, sx[i] * inner, sy[i] * inner, sx[i] * outer, sy[i] * outer);
                }
                draw.SubEntityTraits.Color = 8;
                DrawLine(draw, -_width, 0, _width, 0);
                DrawLine(draw, 0, -_width, 0, _width);
                return true;
            }

            private void DrawLine(WorldDraw draw, double x1, double y1, double x2, double y2)
            {
                Point3d start = new Point3d(_center.X + x1, _center.Y + y1, _center.Z).TransformBy(Ucs);
                Point3d end = new Point3d(_center.X + x2, _center.Y + y2, _center.Z).TransformBy(Ucs);
                draw.Geometry.WorldLine(start, end);
            }
        }

        public void Execute3D()
        {
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;
                Editor editor = document.Editor;

                if (!AskPositive(editor, "Nhập độ rộng miệng gió cấp", SupplyAirDiffuserSetting.DefaultSupplyAirWidth, out double mouthWidth)) return;
                if (!AskPositive(editor, "Nhập độ cao từ miệng gió đến cột ống", SupplyAirDiffuserSetting.DefaultTransitionHeight, out double transitionHeight)) return;
                if (!AskPositive(editor, "Nhập chiều cao cột ống", SupplyAirDiffuserSetting.DefaultColumnHeight, out double columnHeight)) return;
                if (!AskPositive(editor, "Nhập độ rộng miệng cột ống", SupplyAirDiffuserSetting.DefaultColumnWidth, out double columnWidth)) return;

                if (!double.IsFinite(transitionHeight + columnHeight) ||
                    !double.IsFinite(mouthWidth * 1.5) || !double.IsFinite(columnWidth * 1.5))
                {
                    editor.WriteMessage("\nKích thước 3D vượt giới hạn.");
                    return;
                }

                while (true)
                {
                    PromptPointResult result = editor.GetPoint("\nChọn tâm miệng gió cấp 3D : ");
                    if (result.Status != PromptStatus.OK) break;

                    Point3d c = result.Value;
                    Matrix3d ucs = editor.CurrentUserCoordinateSystem;
                    Database database = document.Database;

                    using (Transaction tr = database.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord space = (BlockTableRecord)tr.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                        Solid3d solid = BuildSolid(mouthWidth, transitionHeight, columnHeight, columnWidth);
                        try
                        {
                            solid.TransformBy(Matrix3d.Displacement(new Vector3d(c.X, c.Y, c.Z)));
                            solid.TransformBy(ucs);
                            space.AppendEntity(solid);
                            tr.AddNewlyCreatedDBObject(solid, true);
                        }
                        catch
                        {
                            if (solid.ObjectId.IsNull) solid.Dispose();
                            throw;
                        }

                        tr.Commit();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Info(nameof(SupplyAirDiffuserAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private static Solid3d BuildSolid(double mouthWidth, double transitionHeight, double columnHeight, double columnWidth)
        {
            double t = System.Math.Min(System.Math.Min(mouthWidth, columnWidth) * 0.05, 0.1); // độ dày bích / phần chồng

            Solid3d body = new Solid3d();
            try
            {
                double taper = System.Math.Atan((mouthWidth - columnWidth) / 2 / transitionHeight);
                double half = mouthWidth / 2;
                using (Polyline sq = new Polyline())
                {
                    sq.AddVertexAt(0, new Point2d(-half, -half), 0, 0, 0);
                    sq.AddVertexAt(1, new Point2d(half, -half), 0, 0, 0);
                    sq.AddVertexAt(2, new Point2d(half, half), 0, 0, 0);
                    sq.AddVertexAt(3, new Point2d(-half, half), 0, 0, 0);
                    sq.Closed = true;

                    DBObjectCollection curves = new DBObjectCollection();
                    curves.Add(sq);
                    DBObjectCollection regions = Region.CreateFromCurves(curves);
                    try
                    {
                        body.Extrude((Region)regions[0], transitionHeight, taper);
                    }
                    finally
                    {
                        foreach (DBObject r in regions) r.Dispose();
                    }
                }

                using (Solid3d flange = new Solid3d())
                {
                    flange.CreateBox(mouthWidth * 1.5, mouthWidth * 1.5, t);
                    flange.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, t / 2)));
                    body.BooleanOperation(BooleanOperationType.BoolUnite, flange);
                }

                using (Solid3d column = new Solid3d())
                {
                    column.CreateBox(columnWidth, columnWidth, columnHeight + t);
                    column.TransformBy(Matrix3d.Displacement(new Vector3d(
                        0, 0, transitionHeight - t + (columnHeight + t) / 2)));
                    body.BooleanOperation(BooleanOperationType.BoolUnite, column);
                }

                // Gân nổi tạo cạnh chéo có thể chọn cùng SOLID.
                double ribLength = mouthWidth * System.Math.Sqrt(2) / 4;
                double ribCenter = mouthWidth * 0.625;
                int[] signs = { -1, 1 };
                foreach (int x in signs)
                foreach (int y in signs)
                using (Solid3d rib = new Solid3d())
                {
                    rib.CreateBox(ribLength, t, t);
                    rib.TransformBy(Matrix3d.Rotation(System.Math.Atan2(y, x), Vector3d.ZAxis, Point3d.Origin));
                    rib.TransformBy(Matrix3d.Displacement(new Vector3d(x * ribCenter, y * ribCenter, t)));
                    body.BooleanOperation(BooleanOperationType.BoolUnite, rib);
                }
                return body;
            }
            catch
            {
                body.Dispose();
                throw;
            }
        }

        private static void CreateGroup(Database db, Transaction tr, ObjectIdCollection ids, string description)
        {
            DBDictionary groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            string name = "SupAirDiff_" + Guid.NewGuid().ToString("N");
            Group group = new Group(description, true) { Selectable = true };
            groups.SetAt(name, group);
            tr.AddNewlyCreatedDBObject(group, true);
            foreach (ObjectId id in ids) group.Append(id);
        }

        private static Line AddLine(BlockTableRecord space, Transaction tr, ObjectIdCollection ids,
            Point3d p1, Point3d p2, Matrix3d ucs)
        {
            Line line = new Line(p1.TransformBy(ucs), p2.TransformBy(ucs));
            ids.Add(space.AppendEntity(line));
            tr.AddNewlyCreatedDBObject(line, true);
            return line;
        }

        private static void AddSquare(BlockTableRecord space, Transaction tr, ObjectIdCollection ids,
            Point3d center, double half, double elevation, Matrix3d ucs)
        {
            Point2d[] pts =
            {
                new Point2d(center.X - half, center.Y - half),
                new Point2d(center.X + half, center.Y - half),
                new Point2d(center.X + half, center.Y + half),
                new Point2d(center.X - half, center.Y + half)
            };
            Polyline square = new Polyline();
            for (int i = 0; i < pts.Length; i++)
                square.AddVertexAt(i, pts[i], 0, 0, 0);
            square.Closed = true;
            square.Elevation = elevation;
            square.TransformBy(ucs);
            ids.Add(space.AppendEntity(square));
            tr.AddNewlyCreatedDBObject(square, true);
        }

        private static void AddCornerLines(BlockTableRecord space, Transaction tr, ObjectIdCollection ids,
            Point3d center, double half1, double z1, double half2, double z2, Matrix3d ucs)
        {
            int[] sx = { -1, 1, 1, -1 };
            int[] sy = { -1, -1, 1, 1 };
            for (int i = 0; i < 4; i++)
            {
                AddLine(space, tr, ids,
                    new Point3d(center.X + sx[i] * half1, center.Y + sy[i] * half1, z1),
                    new Point3d(center.X + sx[i] * half2, center.Y + sy[i] * half2, z2), ucs);
            }
        }

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
    }
}
