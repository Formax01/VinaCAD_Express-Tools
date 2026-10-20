using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper
{
    public static class TurningVaneHelper
    {
        /// <param name="pivot">Tâm quay.</param>
        /// <param name="rotation">Góc xoay (radian, ngược chiều kim đồng hồ) quanh tâm quay.</param>
        public static Polyline BuildPolyline(TurningVaneSetting s, Point3d pivot, double rotation)
        {
            double L = s.Length;
            double r = s.InnerRadius;
            double bulge = Math.Tan(Math.PI / 8.0);   // cung 90° => tan(90°/4)
            double x = pivot.X, y = pivot.Y;

            // Hình gốc (góc 0): tâm quay tại pivot, hai cạnh dẫn theo +X và +Y
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x + L, y), -bulge, 0, 0); // A -> B: cung lớn, tâm (L, L), R = L
            pl.AddVertexAt(1, new Point2d(x, y + L), 0, 0, 0);      // B -> T2: đoạn thẳng đuôi
            pl.AddVertexAt(2, new Point2d(x, y + r), bulge, 0, 0);  // T2 -> T1: cung nhỏ, tâm (r, r), R = r
            pl.AddVertexAt(3, new Point2d(x + r, y), 0, 0, 0);      // T1 -> A: đoạn thẳng đuôi
            pl.Closed = true;
            pl.Elevation = pivot.Z;

            // Xoay cả polyline quanh tâm quay
            if (Math.Abs(rotation) > 1e-12)
                pl.TransformBy(Matrix3d.Rotation(rotation, Vector3d.ZAxis, pivot));

            return pl;
        }

        public static ObjectId Create(Database db, TurningVaneSetting s, Point3d pivot, double rotation)
        {
            return CreateMany(db, s, new[] { pivot }, rotation)[0];
        }

        /// <summary>Vẽ nhiều cánh trong một transaction (một lần Undo là hoàn tác cả loạt).</summary>
        public static List<ObjectId> CreateMany(Database db, TurningVaneSetting s,
                                                IEnumerable<Point3d> pivots, double rotation)
        {
            var ids = new List<ObjectId>();
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    bool useLayer = !string.IsNullOrWhiteSpace(s.LayerName);
                    if (useLayer) EnsureLayer(db, tr, s.LayerName);

                    foreach (Point3d pivot in pivots)
                    {
                        Polyline pl = BuildPolyline(s, pivot, rotation);
                        if (useLayer) pl.Layer = s.LayerName;
                        ids.Add(space.AppendEntity(pl));
                        tr.AddNewlyCreatedDBObject(pl, true);
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Không thể tạo entity CLG trong database.", ex);
            }
            return ids;
        }

        private static void EnsureLayer(Database db, Transaction tr, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}