using System;
using System.Collections.Generic;
using System.Globalization;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;

namespace Tools.VinaCad.Helper.Helper
{
    public static class VCDHelper
    {
        private const double Eps = 1e-9;

        /// <summary>N = phần nguyên (Length/Width); offset = phần dư chia đều hai đầu.</summary>
        public static int Calculate(double length, double width, out double offset)
        {
            offset = 0;
            if (length <= 0 || width <= 0)
                return 0;

            int count = (int)Math.Floor(length / width + Eps);
            offset = (length - count * width) / 2.0;
            if (offset < 0) offset = 0;
            return count;
        }

        /// <summary>Tâm block thứ i (toạ độ UCS, chưa xoay).</summary>
        private static Point3d GetCenter(Point3d origin, double width, double offset, int i)
        {
            return new Point3d(
                origin.X + width / 2.0,
                origin.Y + offset + width / 2.0 + i * width,
                origin.Z);
        }

        private static double GetAngleDeg(int i)
        {
            return i % 2 == 0 ? 45.0 : 135.0;
        }

        // ------------------------------------------------------------------
        // Vẽ thật vào bản vẽ
        // ------------------------------------------------------------------
        public static int CreateShape(
            Database db,
            Point3d origin,
            double length,
            double width,
            double rotation,     // radian, quay quanh góc dưới trái (toạ độ UCS)
            Matrix3d ucs)
        {
            int count = Calculate(length, width, out double offset);
            if (db == null || count < 1)
                return 0;

            Matrix3d xform = ucs * Matrix3d.Rotation(rotation, Vector3d.ZAxis, origin);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    ObjectId blk45 = EnsureBlock(tr, bt, width, 45.0);
                    ObjectId blk135 = EnsureBlock(tr, bt, width, 135.0);

                    BlockTableRecord space =
                        (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    // Polyline: theo layer hiện hành, màu ByLayer
                    Polyline rect = CreateRect(db, origin, length, width);
                    rect.TransformBy(xform);
                    space.AppendEntity(rect);
                    tr.AddNewlyCreatedDBObject(rect, true);

                    for (int i = 0; i < count; i++)
                    {
                        BlockReference br = new BlockReference(
                            GetCenter(origin, width, offset, i),
                            i % 2 == 0 ? blk45 : blk135);
                        br.SetDatabaseDefaults(db);
                        br.TransformBy(xform);

                        space.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }

                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(Lang.VCD_MessDialog_KhongTheTaoEntity, ex);
            }

            return count;
        }

        // ------------------------------------------------------------------
        // Entity tạm cho Jig (không ghi vào database). Người gọi tự Dispose.
        // ------------------------------------------------------------------
        public static List<Entity> BuildPreviewEntities(
            Database db,
            Point3d origin,
            double length,
            double width)
        {
            List<Entity> list = new List<Entity>();
            int count = Calculate(length, width, out double offset);

            list.Add(CreateRect(db, origin, length, width));

            for (int i = 0; i < count; i++)
            {
                Point3d c = GetCenter(origin, width, offset, i);
                list.Add(MakeCircle(c, width));
                list.Add(MakeLine(c, width, GetAngleDeg(i)));
            }

            return list;
        }

        // ------------------------------------------------------------------
        private static Polyline CreateRect(Database db, Point3d origin, double length, double width)
        {
            Polyline rect = new Polyline(4);
            rect.AddVertexAt(0, new Point2d(origin.X, origin.Y), 0, 0, 0);
            rect.AddVertexAt(1, new Point2d(origin.X + width, origin.Y), 0, 0, 0);
            rect.AddVertexAt(2, new Point2d(origin.X + width, origin.Y + length), 0, 0, 0);
            rect.AddVertexAt(3, new Point2d(origin.X, origin.Y + length), 0, 0, 0);
            rect.Closed = true;
            rect.Elevation = origin.Z;

            rect.SetDatabaseDefaults(db);
            rect.LayerId = db.Clayer;                                  // layer hiện hành
            rect.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256); // màu theo layer
            return rect;
        }

        private static Circle MakeCircle(Point3d center, double width)
        {
            Circle c = new Circle(center, Vector3d.ZAxis, width / 10.0);
            c.Color = Color.FromColorIndex(ColorMethod.ByAci, 1); // đỏ
            return c;
        }

        private static Line MakeLine(Point3d center, double width, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(a) * width / 2.0;
            double dy = Math.Sin(a) * width / 2.0;

            Line l = new Line(
                new Point3d(center.X - dx, center.Y - dy, center.Z),
                new Point3d(center.X + dx, center.Y + dy, center.Z));
            l.Color = Color.FromColorIndex(ColorMethod.ByAci, 1); // đỏ
            return l;
        }

        /// <summary>
        /// Block: 1 hình tròn R = width/10 + 1 đường thẳng dài = width qua tâm,
        /// nghiêng angleDeg. Entity trong block luôn màu đỏ.
        /// </summary>
        private static ObjectId EnsureBlock(Transaction tr, BlockTable bt, double width, double angleDeg)
        {
            // Prefix mới "VCDR" để không dùng lại block cũ (chưa đặt màu đỏ)
            string name = string.Format(
                CultureInfo.InvariantCulture,
                "VCDR_W{0}_A{1}",
                width.ToString("0.######", CultureInfo.InvariantCulture).Replace('.', '_'),
                (int)angleDeg);

            if (bt.Has(name))
                return bt[name];

            bt.UpgradeOpen();

            BlockTableRecord btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            ObjectId id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            Circle circle = MakeCircle(Point3d.Origin, width);
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            Line line = MakeLine(Point3d.Origin, width, angleDeg);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);

            return id;
        }
    }
}