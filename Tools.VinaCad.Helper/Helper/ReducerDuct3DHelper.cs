using System;
using System.Collections.Generic;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace Tools.VinaCad.Helper.Helper
{
    public enum DuctYAlign { Center, Left, Right }   // theo trục OY
    public enum DuctZAlign { Top, Middle, Bottom }   // theo trục OZ

    public class ReducerDuct3DModel
    {
        public double Length { get; set; }   // OX
        public double Width { get; set; }    // OY
        public double Height { get; set; }   // OZ
        public double Width2 { get; set; }   // OY
        public double Height2 { get; set; }  // OZ
        public DuctYAlign YAlign { get; set; } = DuctYAlign.Center;
        public DuctZAlign ZAlign { get; set; } = DuctZAlign.Middle;
    }

    public static class ReducerDuct3DHelper
    {
        // 6 mặt, chỉ số đỉnh bắt đầu từ 1 (quy ước PolyFaceMesh)
        // 1-4: mặt đáy (x = 0), 5-8: mặt trên (x = L)
        private static readonly int[][] Faces =
        {
            new[] { 1, 2, 3, 4 },   // mặt đáy
            new[] { 5, 6, 7, 8 },   // mặt trên
            new[] { 1, 2, 6, 5 },
            new[] { 2, 3, 7, 6 },
            new[] { 3, 4, 8, 7 },
            new[] { 4, 1, 5, 8 },
        };

        /// <summary>8 đỉnh trong hệ local: gốc = tâm mặt đáy, trục ống = +X.</summary>
        public static Point3d[] BuildLocalVertices(ReducerDuct3DModel m)
        {
            double w = m.Width, h = m.Height, w2 = m.Width2, h2 = m.Height2;

            // Tâm mặt trên lệch so với tâm mặt đáy (canh mép trùng nhau)
            double cy = 0;
            switch (m.YAlign)
            {
                case DuctYAlign.Left: cy = (w - w2) / 2.0; break; // mép +Y trùng
                case DuctYAlign.Right: cy = -(w - w2) / 2.0; break; // mép -Y trùng
            }

            double cz = 0;
            switch (m.ZAlign)
            {
                case DuctZAlign.Top: cz = (h - h2) / 2.0; break;
                case DuctZAlign.Bottom: cz = -(h - h2) / 2.0; break;
            }

            return new[]
            {
                // đáy (x = 0)
                new Point3d(0, -w / 2, -h / 2),
                new Point3d(0,  w / 2, -h / 2),
                new Point3d(0,  w / 2,  h / 2),
                new Point3d(0, -w / 2,  h / 2),
                // mặt trên (x = L)
                new Point3d(m.Length, cy - w2 / 2, cz - h2 / 2),
                new Point3d(m.Length, cy + w2 / 2, cz - h2 / 2),
                new Point3d(m.Length, cy + w2 / 2, cz + h2 / 2),
                new Point3d(m.Length, cy - w2 / 2, cz + h2 / 2),
            };
        }

        /// <summary>Đổi đỉnh local sang WCS: xoay quanh trục Z tại origin.</summary>
        public static Point3d[] ToWorld(Point3d[] local, Point3d origin, double angle)
        {
            Point3d[] result = new Point3d[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                Vector3d v = new Vector3d(local[i].X, local[i].Y, local[i].Z)
                    .RotateBy(angle, Vector3d.ZAxis);
                result[i] = origin + v;
            }
            return result;
        }

        public static IEnumerable<Point3d[]> GetFacePolygons(Point3d[] worldVertices)
        {
            foreach (int[] f in Faces)
            {
                Point3d[] poly = new Point3d[f.Length];
                for (int i = 0; i < f.Length; i++)
                    poly[i] = worldVertices[f[i] - 1];
                yield return poly;
            }
        }

        /// <summary>Tạo PolyFaceMesh trong Model Space.</summary>
        public static ObjectId CreateMesh(Database db, Point3d[] worldVertices)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                PolyFaceMesh mesh = new PolyFaceMesh();
                mesh.SetDatabaseDefaults(db);

                ObjectId id = ms.AppendEntity(mesh);
                tr.AddNewlyCreatedDBObject(mesh, true);

                foreach (Point3d p in worldVertices)
                {
                    PolyFaceMeshVertex v = new PolyFaceMeshVertex(p);
                    mesh.AppendVertex(v);
                    tr.AddNewlyCreatedDBObject(v, true);
                }

                foreach (int[] f in Faces)
                {
                    FaceRecord fr = new FaceRecord(
                        (short)f[0], (short)f[1], (short)f[2], (short)f[3]);
                    mesh.AppendFaceRecord(fr);          // thay cho mesh.AppendVertex(fr)
                    tr.AddNewlyCreatedDBObject(fr, true);
                }

                tr.Commit();
                return id;
            }
        }

    }

    /// <summary>Jig ướm hình: xoay quanh trục Z đi qua điểm đầu.</summary>
    public sealed class ReducerDuct3DJig : DrawJig
    {
        private readonly Point3d[] _local;
        private readonly Point3d _origin;       
        private readonly Matrix3d _ucs2wcs;
        private Point3d _lastCursor;             

        public double Angle { get; private set; }

        public ReducerDuct3DJig(Point3d[] local, Point3d origin, Matrix3d ucs2wcs)
        {
            _local = local;
            _origin = origin;
            _ucs2wcs = ucs2wcs;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opt = new JigPromptPointOptions(
                "\nXoay hình quanh điểm đầu, click để chọn vị trí: ")
            {
                UseBasePoint = true,
                // BasePoint của prompt tính theo UCS nên phải đổi ngược lại
                BasePoint = _origin.TransformBy(_ucs2wcs.Inverse()),
                UserInputControls = UserInputControls.Accept3dCoordinates
                                  | UserInputControls.NullResponseAccepted
            };

            PromptPointResult r = prompts.AcquirePoint(opt);
            if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;

            Point3d cursorWcs = r.Value.TransformBy(_ucs2wcs);   

            if (cursorWcs.DistanceTo(_lastCursor) < 1e-9)
                return SamplerStatus.NoChange;

            _lastCursor = cursorWcs;
            Vector3d dir = cursorWcs - _origin;
            if (dir.Length > 1e-9)
                Angle = Math.Atan2(dir.Y, dir.X);   

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            
            Point3d[] world = ReducerDuct3DHelper.ToWorld(_local, _origin, Angle);

            foreach (Point3d[] polygon in ReducerDuct3DHelper.GetFacePolygons(world))
            {
                for (int i = 0; i < polygon.Length; i++)
                    DrawSegment(draw, polygon[i], polygon[(i + 1) % polygon.Length]);
            }

            return true;
        }

        private static void DrawSegment(WorldDraw draw, Point3d start, Point3d end)
        {
            using (Line line = new Line(start, end))
                draw.Geometry.Draw(line);
        }
    }
}