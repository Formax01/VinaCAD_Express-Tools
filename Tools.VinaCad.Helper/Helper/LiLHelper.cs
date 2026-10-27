using System;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;

namespace Tools.VinaCad.Helper.Helper
{
    public static class LiLHelper
    {
        /// <summary>
        /// p1, p2: điểm theo UCS hiện hành. Trả về số đoạn thẳng đã vẽ.
        /// </summary>
        public static int DrawLiL(Database db, Point3d p1, Point3d p2, double l, Matrix3d ucs)
        {
            if (db == null || l <= 0)
                return 0;

            // Tính toán trong UCS (mặt phẳng XY của UCS), z lấy theo điểm 1
            double z = p1.Z;
            Vector3d v = new Vector3d(p2.X - p1.X, p2.Y - p1.Y, 0);

            if (v.Length < 1e-9)
                return 0;

            Vector3d dir = v.GetNormal();
            Vector3d perp = new Vector3d(-dir.Y, dir.X, 0);

            Point3d a = new Point3d(p1.X, p1.Y, z);
            Point3d b = new Point3d(p2.X, p2.Y, z);

            // Đoạn 1 (giữa, màu đỏ): kéo dài mỗi đầu L
            Point3d c1 = a - dir * l;
            Point3d c2 = b + dir * l;

            // Hai đoạn song song, dài bằng |P1P2|, cách đoạn giữa L
            Point3d u1 = a + perp * l;
            Point3d u2 = b + perp * l;
            Point3d d1 = a - perp * l;
            Point3d d2 = b - perp * l;

            int count = 0;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = tr.GetObject(
                        db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                    if (space == null)
                        return 0;

                    // Đường giữa: màu đỏ (ACI 1)
                    AddLine(tr, space, db, c1, c2, ucs, Color.FromColorIndex(ColorMethod.ByLayer, 256));

                    // Hai đoạn song song, dài bằng |P1P2|, cách đoạn giữa L
                    AddLine(tr, space, db, u1, u2, ucs, Color.FromColorIndex(ColorMethod.ByAci, 7));
                    AddLine(tr, space, db, d1, d2, ucs, Color.FromColorIndex(ColorMethod.ByAci, 7));

                    count = 3;
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(Lang.LIL_MessDialog_KhongTheTaoEntity, ex);
            }

            return count;
        }

        private static void AddLine(
            Transaction tr,
            BlockTableRecord space,
            Database db,
            Point3d start,
            Point3d end,
            Matrix3d ucs,
            Color color)
        {
            // Đổi UCS -> WCS một lần trước khi ghi vào database
            Line line = new Line(start.TransformBy(ucs), end.TransformBy(ucs));
            line.SetDatabaseDefaults(db);
            line.Color = color;

            space.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }
    }
}