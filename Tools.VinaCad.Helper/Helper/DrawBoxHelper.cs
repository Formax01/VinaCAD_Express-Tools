using System;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace Tools.VinaCad.Helper.Helper
{
    public class DrawBoxHelper
    {
        // Cặp chỉ số đỉnh của 12 cạnh (dùng cho Jig)
        public static readonly int[][] Edges =
        {
            new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, // đáy
            new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 }, // đỉnh
            new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }  // cạnh đứng
        };

        /// <summary>
        /// 8 đỉnh theo UCS, đã xoay quanh điểm bắt đầu (trục Z của UCS).
        /// </summary>
        public static Point3d[] GetBoxPointsUcs(
            Point3d startUcs, double length, double width, double height, double angle)
        {
            Point3d[] pts =
            {
                new Point3d(startUcs.X,          startUcs.Y,         startUcs.Z),
                new Point3d(startUcs.X + length, startUcs.Y,         startUcs.Z),
                new Point3d(startUcs.X + length, startUcs.Y + width, startUcs.Z),
                new Point3d(startUcs.X,          startUcs.Y + width, startUcs.Z),
                new Point3d(startUcs.X,          startUcs.Y,         startUcs.Z + height),
                new Point3d(startUcs.X + length, startUcs.Y,         startUcs.Z + height),
                new Point3d(startUcs.X + length, startUcs.Y + width, startUcs.Z + height),
                new Point3d(startUcs.X,          startUcs.Y + width, startUcs.Z + height)
            };

            Matrix3d rot = Matrix3d.Rotation(angle, Vector3d.ZAxis, startUcs);

            for (int i = 0; i < pts.Length; i++)
                pts[i] = pts[i].TransformBy(rot);

            return pts;
        }

        public static int CreateBox(
            Database db,
            Matrix3d ucs,
            Point3d startUcs,
            double length,
            double width,
            double height,
            double angle)
        {
            if (db == null || length <= 0 || width <= 0 || height <= 0)
                return 0;

            Point3d[] ucsPoints = GetBoxPointsUcs(startUcs, length, width, height, angle);

            short[][] faces =
            {
                new short[] { 1, 4, 3, 2 }, // đáy
                new short[] { 5, 6, 7, 8 }, // đỉnh
                new short[] { 1, 2, 6, 5 }, // trước
                new short[] { 2, 3, 7, 6 }, // phải
                new short[] { 3, 4, 8, 7 }, // sau
                new short[] { 4, 1, 5, 8 }  // trái
            };

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (space == null)
                    return 0;

                PolyFaceMesh mesh = new PolyFaceMesh();
                mesh.SetDatabaseDefaults(db);

                space.AppendEntity(mesh);
                tr.AddNewlyCreatedDBObject(mesh, true);

                foreach (Point3d p in ucsPoints)
                {
                    PolyFaceMeshVertex vertex = new PolyFaceMeshVertex(p.TransformBy(ucs));
                    mesh.AppendVertex(vertex);
                    tr.AddNewlyCreatedDBObject(vertex, true);
                }

                foreach (short[] f in faces)
                {
                    FaceRecord face = new FaceRecord(f[0], f[1], f[2], f[3]);
                    mesh.AppendFaceRecord(face);
                    tr.AddNewlyCreatedDBObject(face, true);
                }

                tr.Commit();
                return 1;
            }
        }

    }

    public sealed class DrawBoxJig : DrawJig
        {
            private readonly Matrix3d _ucs;
            private readonly Point3d _startUcs;
            private readonly double _length;
            private readonly double _width;
            private readonly double _height;

            public double Angle { get; private set; }

            public DrawBoxJig(
                Matrix3d ucs, Point3d startUcs,
                double length, double width, double height)
            {
                _ucs = ucs;
                _startUcs = startUcs;
                _length = length;
                _width = width;
                _height = height;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptAngleOptions opt =
                    new JigPromptAngleOptions("\nXoay hộp quanh điểm bắt đầu (click để đặt): ")
                    {
                        BasePoint = _startUcs,
                        UseBasePoint = true,
                        Cursor = CursorType.RubberBand,
                        UserInputControls = UserInputControls.NullResponseAccepted
                    };

                PromptDoubleResult res = prompts.AcquireAngle(opt);

                if (res.Status == PromptStatus.Cancel)
                    return SamplerStatus.Cancel;

                if (res.Status != PromptStatus.OK)
                    return SamplerStatus.NoChange;

                if (Math.Abs(res.Value - Angle) < 1e-9)
                    return SamplerStatus.NoChange;

                Angle = res.Value;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                Point3d[] pts = DrawBoxHelper.GetBoxPointsUcs(
                    _startUcs, _length, _width, _height, Angle);

                foreach (int[] e in DrawBoxHelper.Edges)
                {
                    draw.Geometry.WorldLine(
                        pts[e[0]].TransformBy(_ucs),
                        pts[e[1]].TransformBy(_ucs));
                }

                return true;
        }
    }
}