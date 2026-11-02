using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    
    public static class DuctHelper
    {
        private const double ZeroTolerance = 1e-9;

        /// <summary>
        /// Tra ve huong don vi trong mat phang XY. Khi Ortho bat, huong duoc ep ve truc gan nhat.
        /// </summary>
        public static Vector3d GetDirection(Point3d start, Point3d target, bool ortho)
        {
            double deltaX = target.X - start.X;
            double deltaY = target.Y - start.Y;

            if (Math.Abs(deltaX) < ZeroTolerance && Math.Abs(deltaY) < ZeroTolerance)
                return Vector3d.XAxis;

            if (ortho)
            {
                if (Math.Abs(deltaX) >= Math.Abs(deltaY))
                    deltaY = 0;
                else
                    deltaX = 0;
            }

            return new Vector3d(deltaX, deltaY, 0).GetNormal();
        }

        /// <summary>
        /// Tao bon goc hinh chu nhat tu diem neo o canh duoi ben phai cua chieu di.
        /// </summary>
        public static Point3d[] GetCorners(Point3d start, Vector3d direction, double length, double width)
        {
            Vector3d along = direction.GetNormal();
            Vector3d left = along.RotateBy(Math.PI / 2, Vector3d.ZAxis);
            Point3d end = start + along * length;

            return new[] { start, end, end + left * width, start + left * width };
        }

        /// <summary>
        /// Tao bon goc khi hai diem dau vao tao thanh mot canh cua ong gio.
        /// </summary>
        public static Point3d[] GetCornersFromSide(Point3d first, Point3d second, double offset)
        {
            Vector3d along = (second - first).GetNormal();
            Vector3d shift = along.RotateBy(Math.PI / 2, Vector3d.ZAxis) * offset;

            return new[] { first, second, second + shift, first + shift };
        }

        public static ObjectId CreateDuct(Database database, DuctModel model, Matrix3d ucsToWcs)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            return CreateClosedPolyline(
                database,
                GetCorners(model.StartPoint, model.Direction, model.Length, model.Width),
                model.Layer,
                ucsToWcs);
        }

        /// <summary>
        /// Ghi mot polyline kin va dam bao layer dich ton tai trong cung transaction.
        /// </summary>
        public static ObjectId CreateClosedPolyline(
            Database database,
            Point3d[] vertices,
            string layer,
            Matrix3d ucsToWcs)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (vertices.Length < 3) throw new ArgumentException("A closed polyline requires at least three vertices.", nameof(vertices));
            if (string.IsNullOrWhiteSpace(layer)) throw new ArgumentException("A layer name is required.", nameof(layer));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            EnsureLayer(database, transaction, layer);

            BlockTableRecord modelSpace = GetModelSpace(database, transaction);
            var polyline = new Polyline();
            for (int index = 0; index < vertices.Length; index++)
            {
                Point3d vertex = vertices[index];
                polyline.AddVertexAt(index, new Point2d(vertex.X, vertex.Y), 0, 0, 0);
            }

            polyline.Closed = true;
            polyline.Elevation = vertices[0].Z;
            polyline.Layer = layer;
            polyline.TransformBy(ucsToWcs);

            ObjectId id = modelSpace.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);
            transaction.Commit();
            return id;
        }

        internal static BlockTableRecord GetModelSpace(Database database, Transaction transaction)
        {
            BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }

        internal static void EnsureLayer(Database database, Transaction transaction, string layerName)
        {
            LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            if (layerTable.Has(layerName)) return;

            layerTable.UpgradeOpen();
            var layer = new LayerTableRecord { Name = layerName };
            layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
        }
    }
}
