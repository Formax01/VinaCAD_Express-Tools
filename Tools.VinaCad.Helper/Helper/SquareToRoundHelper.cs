using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper.Helper
{
    /// <summary>
    /// Tinh toan va ve chi tiet chuyen tu tiet dien vuong sang tron.
    /// </summary>
    public static class SquareToRoundHelper
    {
        private static readonly double DefaultSlope = 20.0 * Math.PI / 180.0;

        public static double GetAutoLength(double bigSize, double smallSize)
        {
            return (bigSize - smallSize) / (2.0 * Math.Tan(DefaultSlope));
        }

        /// <summary>
        /// Thu tu diem: goc tren/dau lon, goc duoi/dau lon, goc duoi/dau nho, goc tren/dau nho.
        /// </summary>
        public static Point3d[] GetCorners(
            Point3d start,
            Vector3d direction,
            double bigSize,
            double smallSize,
            double length)
        {
            Vector3d along = direction.GetNormal();
            Vector3d left = along.RotateBy(Math.PI / 2, Vector3d.ZAxis);
            Point3d bigTop = start + left * bigSize;
            Point3d smallCenter = start + left * (bigSize / 2.0) + along * length;

            return new[]
            {
                bigTop,
                start,
                smallCenter - left * (smallSize / 2.0),
                smallCenter + left * (smallSize / 2.0)
            };
        }

        /// <summary>
        /// Duong polyline lien tuc qua vien va cac duong chia cua chi tiet.
        /// </summary>
        public static Point3d[] GetPath(Point3d[] corners)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (corners.Length != 4) throw new ArgumentException("Exactly four corners are required.", nameof(corners));

            Point3d bigTop = corners[0];
            Point3d bigBottom = corners[1];
            Point3d smallBottom = corners[2];
            Point3d smallTop = corners[3];
            Vector3d smallSide = smallBottom - smallTop;
            Point3d upperQuarter = smallTop + smallSide * 0.25;
            Point3d middle = smallTop + smallSide * 0.50;
            Point3d lowerQuarter = smallTop + smallSide * 0.75;

            return new[]
            {
                lowerQuarter, smallBottom, bigBottom, bigTop, smallTop, upperQuarter,
                bigTop, middle, bigBottom, lowerQuarter, middle, upperQuarter
            };
        }

        public static void Create(Database database, Point3d[] corners, string layer)
        {
            Point3d[] path = GetPath(corners);
            using Transaction transaction = database.TransactionManager.StartTransaction();
            DuctHelper.EnsureLayer(database, transaction, layer);

            BlockTableRecord modelSpace = DuctHelper.GetModelSpace(database, transaction);
            var polyline = new Polyline();
            for (int index = 0; index < path.Length; index++)
            {
                Point3d point = path[index];
                polyline.AddVertexAt(index, new Point2d(point.X, point.Y), 0, 0, 0);
            }

            polyline.Closed = false;
            polyline.Elevation = path[0].Z;
            polyline.Layer = layer;
            modelSpace.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);
            transaction.Commit();
        }
    }
}
