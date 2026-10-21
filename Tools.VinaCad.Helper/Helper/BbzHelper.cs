using System;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;
using WorldDraw = Teigha.GraphicsInterface.WorldDraw;

namespace Tools.VinaCad.Helper.Helper
{
    public static class BbzHelper
    {
        /// <summary>Đoạn thẳng ở hai đầu = min(max(W)/2, L/4).</summary>
        public static double GetStubLength(BbzModel m)
        {
            return Math.Min(Math.Max(m.Width1, m.Width2) * 0.5, m.Length * 0.25);
        }

        public static bool Validate(BbzModel m, out string error)
        {
            error = null;

            if (m.Width1 <= 0 || m.Width2 <= 0) error = "Bề rộng ống gió phải > 0.";
            else if (m.Offset <= 0) error = "Cao độ lượn phải > 0.";
            else if (m.Length <= 0) error = "Chiều dài L phải > 0.";
            else if (m.Height <= 0) error = "Chiều cao ống gió phải > 0.";

            return error == null;
        }

        /// <summary>
        /// Biên dạng mặt bằng (8 đỉnh) trong hệ toạ độ cục bộ:
        /// gốc = điểm trên đầu ống 1, trục X = hướng ống, ống 1 nằm ở y ∈ [-W1, 0].
        /// "Trái" = +Y, "Phải" = -Y.
        /// </summary>
        public static Point3d[] GetLocalOutline(BbzModel m)
        {
            double s = GetStubLength(m);
            double sign = m.Side == BbzSide.Left ? 1.0 : -1.0;

            double c1 = -m.Width1 / 2.0;
            double c2 = c1 + sign * m.Offset;

            double t1 = 0.0;
            double b1 = -m.Width1;
            double t2 = c2 + m.Width2 / 2.0;
            double b2 = c2 - m.Width2 / 2.0;

            return new[]
            {
                new Point3d(0,            t1, 0),
                new Point3d(s,            t1, 0),
                new Point3d(m.Length - s, t2, 0),
                new Point3d(m.Length,     t2, 0),
                new Point3d(m.Length,     b2, 0),
                new Point3d(m.Length - s, b2, 0),
                new Point3d(s,            b1, 0),
                new Point3d(0,            b1, 0)
            };
        }

        /// <summary>
        /// local -> UCS (xoay quanh Z, tịnh tiến tới StartPoint) -> WCS.
        /// Chuyển WCS đúng 1 lần ở đây nên không bị lệch khi UCS bị xoay/dời.
        /// </summary>
        public static Matrix3d BuildTransform(Matrix3d ucsToWcs, Point3d startUcs, double angle)
        {
            return ucsToWcs
                 * Matrix3d.Displacement(startUcs.GetAsVector())
                 * Matrix3d.Rotation(angle, Vector3d.ZAxis, Point3d.Origin);
        }

        /// <summary>Tạo Solid3d bằng cách extrude biên dạng mặt bằng theo chiều cao.</summary>
        public static Solid3d CreateDuctSolid(BbzModel m, Matrix3d localToWcs)
        {
            Point3d[] outline = GetLocalOutline(m);

            using (Polyline pl = new Polyline())
            {
                for (int i = 0; i < outline.Length; i++)
                    pl.AddVertexAt(i, new Point2d(outline[i].X, outline[i].Y), 0, 0, 0);

                pl.Closed = true;

                DBObjectCollection curves = new DBObjectCollection();
                curves.Add(pl);

                DBObjectCollection regions = Region.CreateFromCurves(curves);

                try
                {
                    if (regions.Count == 0)
                        throw new InvalidOperationException("Không tạo được Region từ biên dạng ống gió.");

                    Region region = (Region)regions[0];

                    Solid3d solid = new Solid3d();
                    solid.Extrude(region, m.Height, 0.0);
                    solid.TransformBy(localToWcs);

                    return solid;
                }
                finally
                {
                    foreach (DBObject o in regions)
                        o.Dispose();
                }
            }
        }

        public static ObjectId AppendToCurrentSpace(Database db, Entity ent)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Giữ layer hiện hành của bản vẽ cho đối tượng mới.
                ent.SetDatabaseDefaults(db);

                BlockTableRecord space =
                    (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                ObjectId id = space.AppendEntity(ent);
                tr.AddNewlyCreatedDBObject(ent, true);

                tr.Commit();
                return id;
            }
        }
    }

    public sealed class BbzPlacementJig : DrawJig
    {
        private readonly BbzModel _model;
        private readonly Matrix3d _ucsToWcs;
        private readonly Point3d[] _outline;
        private Point3d _lastPoint;

        /// <summary>Góc xoay trong mặt phẳng XY của UCS.</summary>
        public double Angle { get; private set; }

        public BbzPlacementJig(BbzModel model, Matrix3d ucsToWcs)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _outline = BbzHelper.GetLocalOutline(model);
            _lastPoint = model.StartPoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions options = new JigPromptPointOptions("\nChọn hướng đặt ống gió: ")
            {
                UseBasePoint = true,
                BasePoint = _model.StartPoint,
                Cursor = CursorType.RubberBand
            };

            PromptPointResult result = prompts.AcquirePoint(options);

            if (result.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            Point3d p = result.Value;
            double dx = p.X - _model.StartPoint.X;
            double dy = p.Y - _model.StartPoint.Y;

            if (Math.Sqrt(dx * dx + dy * dy) < 1e-9)
                return SamplerStatus.NoChange;

            if (p.DistanceTo(_lastPoint) < 1e-9)
                return SamplerStatus.NoChange;

            _lastPoint = p;
            Angle = Math.Atan2(dy, dx);

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            Matrix3d xf = BbzHelper.BuildTransform(_ucsToWcs, _model.StartPoint, Angle);

            int n = _outline.Length;
            Point3d[] bottom = new Point3d[n];
            Point3d[] top = new Point3d[n];

            for (int i = 0; i < n; i++)
            {
                Point3d p = _outline[i];
                bottom[i] = p.TransformBy(xf);
                top[i] = new Point3d(p.X, p.Y, _model.Height).TransformBy(xf);
            }

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;

                DrawSegment(draw, bottom[i], bottom[j]); // đáy
                DrawSegment(draw, top[i], top[j]);       // đỉnh
                DrawSegment(draw, bottom[i], top[i]);    // cạnh đứng
            }

            return true;
        }

        private static void DrawSegment(WorldDraw draw, Point3d a, Point3d b)
        {
            using (Line line = new Line(a, b))
            {
                draw.Geometry.Draw(line);
    }
    }
    }
}