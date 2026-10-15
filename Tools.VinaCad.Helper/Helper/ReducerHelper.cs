using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    public static class ReducerHelper
    {
        /// Hướng đơn vị trong mặt phẳng XY; nếu ortho bật thì ép về trục X/Y gần nhất.
        public static bool TryGetDirection(
            Point3d start, Point3d end, bool ortho, out Vector3d direction)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            direction = Vector3d.XAxis;

            if (ortho)
            {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0;
            }

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return false;

            direction = new Vector3d(dx / len, dy / len, 0);
            return true;
        }

        /// Vẽ chi tiết giảm/tăng. Các điểm tính theo UCS, sau đó đổi sang WCS.
        /// 4 đỉnh theo UCS, thứ tự a1 → b1 → b2 → a2.
        public static Point3d[] GetCorners(ReducerModel m)
        {
            Vector3d along = m.Direction;
            Vector3d left = along.RotateBy(Math.PI / 2, Vector3d.ZAxis);   // pháp tuyến trái

            // Tâm xoay = góc phải của đầu 1 = StartPoint; chi tiết mở rộng về bên trái hướng đi.
            Point3d a1 = m.StartPoint;                 // đầu 1, góc phải (tâm xoay)
            Point3d a2 = a1 + left * m.Width1;         // đầu 1, góc trái

            
            double offset = m.Side switch
            {
                ReducerSide.Right => 0,
                ReducerSide.Left => m.Width1 - m.Width2,
                _ => (m.Width1 - m.Width2) / 2
            };

            Point3d b1 = a1 + along * m.Length + left * offset;   // đầu 2, góc phải
            Point3d b2 = b1 + left * m.Width2;                    // đầu 2, góc trái

            return new[] { a1, b1, b2, a2 };
        }
        public static ObjectId Draw(Database db, Matrix3d ucsToWcs, ReducerModel m)
        {
            Point3d[] c = GetCorners(m);   // a1, b1, b2, a2 (theo UCS)

            using Transaction tr = db.TransactionManager.StartTransaction();

            EnsureLayer(db, tr, m.Layer);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var pl = new Polyline();
            for (int i = 0; i < c.Length; i++)
                pl.AddVertexAt(i, new Point2d(c[i].X, c[i].Y), 0, 0, 0);
            pl.Closed = true;
            pl.Elevation = c[0].Z;
            pl.Layer = m.Layer;

            // Đổi từ UCS sang WCS (giữ đúng khi người dùng xoay UCS).
            pl.TransformBy(ucsToWcs);

            ObjectId id = ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            tr.Commit();
            return id;
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = layerName };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }
}