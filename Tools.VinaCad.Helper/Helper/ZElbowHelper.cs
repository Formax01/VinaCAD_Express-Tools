using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    /// <summary>
    /// Tinh toan va ve ong gio chu Z duoc tao boi hai cut cong.
    /// </summary>
    public static class ZElbowHelper
    {
        private const double StandardRadiusRatio = 1.5;
        private const double MinimumRadiusRatio = 1.0;
        private const double Tolerance = 1e-9;

        public static double GetMinimumLength(double width, double offset)
        {
            double absoluteOffset = Math.Abs(offset);
            double minimumRadius = MinimumRadiusRatio * width;
            double radiusBound = Math.Sqrt(Math.Max(0, 4.0 * absoluteOffset * minimumRadius - absoluteOffset * absoluteOffset));
            return Math.Max(absoluteOffset, radiusBound);
        }

        public static ZElbowShape? Build(Point3d start, Vector3d direction, double width, double offset, double length)
        {
            if (!TryGetLayout(width, offset, length, out double angle, out double radius, out double straightLength))
                return null;

            Vector3d along = direction.GetNormal();
            Vector3d left = along.RotateBy(Math.PI / 2, Vector3d.ZAxis);
            Point3d centerStart = start - left * (width / 2.0);
            int offsetSign = offset >= 0 ? 1 : -1;
            double signedAngle = offsetSign * angle;

            Point2d startCenter = new Point2d(0, 0);
            Point2d firstElbowEnd = new Point2d(
                radius * Math.Sin(angle),
                offsetSign * radius * (1.0 - Math.Cos(angle)));
            Point2d secondElbowStart = new Point2d(
                firstElbowEnd.X + straightLength * Math.Cos(angle),
                firstElbowEnd.Y + offsetSign * straightLength * Math.Sin(angle));
            Point2d endCenter = new Point2d(length, offset);

            Point3d Wall(Point2d point, double heading, double distance)
            {
                double x = point.X - distance * Math.Sin(heading);
                double y = point.Y + distance * Math.Cos(heading);
                return centerStart + along * x + left * y;
            }

            double halfWidth = width / 2.0;
            Point3d startLeft = Wall(startCenter, 0, halfWidth);
            Point3d firstLeft = Wall(firstElbowEnd, signedAngle, halfWidth);
            Point3d secondLeft = Wall(secondElbowStart, signedAngle, halfWidth);
            Point3d endLeft = Wall(endCenter, 0, halfWidth);
            Point3d startRight = Wall(startCenter, 0, -halfWidth);
            Point3d firstRight = Wall(firstElbowEnd, signedAngle, -halfWidth);
            Point3d secondRight = Wall(secondElbowStart, signedAngle, -halfWidth);
            Point3d endRight = Wall(endCenter, 0, -halfWidth);

            double firstBulge = offsetSign * Math.Tan(angle / 4.0);
            double secondBulge = -firstBulge;
            bool hasStraight = straightLength > 1e-6;
            var vertices = new List<Point3d>();
            var bulges = new List<double>();

            void AddVertex(Point3d point, double bulge)
            {
                vertices.Add(point);
                bulges.Add(bulge);
            }

            AddVertex(startLeft, firstBulge);
            if (hasStraight)
            {
                AddVertex(firstLeft, 0);
                AddVertex(secondLeft, secondBulge);
            }
            else
            {
                AddVertex(firstLeft, secondBulge);
            }

            AddVertex(endLeft, 0);
            AddVertex(endRight, -secondBulge);
            if (hasStraight)
            {
                AddVertex(secondRight, 0);
                AddVertex(firstRight, -firstBulge);
            }
            else
            {
                AddVertex(firstRight, -firstBulge);
            }

            AddVertex(startRight, 0);

            var cutLines = new List<Point3d[]> { new[] { firstLeft, firstRight } };
            if (hasStraight) cutLines.Add(new[] { secondLeft, secondRight });

            return new ZElbowShape
            {
                Vertices = vertices.ToArray(),
                Bulges = bulges.ToArray(),
                CutLines = cutLines.ToArray(),
                Theta = angle,
                Radius = radius,
                Straight = straightLength
            };
        }

        public static List<Point3d> TessellateBulge(Point3d start, Point3d end, double bulge, int steps)
        {
            var points = new List<Point3d> { start };
            if (Math.Abs(bulge) < Tolerance)
            {
                points.Add(end);
                return points;
            }

            double arcAngle = 4.0 * Math.Atan(bulge);
            double chord = start.DistanceTo(end);
            double radius = chord / (2.0 * Math.Sin(Math.Abs(arcAngle) / 2.0));
            double unitX = (end.X - start.X) / chord;
            double unitY = (end.Y - start.Y) / chord;
            double centerOffset = (chord / 2.0) / Math.Tan(arcAngle / 2.0);
            double centerX = (start.X + end.X) / 2.0 - unitY * centerOffset;
            double centerY = (start.Y + end.Y) / 2.0 + unitX * centerOffset;
            double initialAngle = Math.Atan2(start.Y - centerY, start.X - centerX);

            for (int index = 1; index <= steps; index++)
            {
                double currentAngle = initialAngle + arcAngle * index / steps;
                points.Add(new Point3d(
                    centerX + radius * Math.Cos(currentAngle),
                    centerY + radius * Math.Sin(currentAngle),
                    start.Z));
            }

            return points;
        }

        public static void Create(Database database, ZElbowShape shape, string layer, Matrix3d ucsToWcs)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (shape.Vertices == null || shape.Bulges == null || shape.Vertices.Length != shape.Bulges.Length)
                throw new ArgumentException("The Z-elbow shape must contain one bulge value per vertex.", nameof(shape));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            DuctHelper.EnsureLayer(database, transaction, layer);
            BlockTableRecord modelSpace = DuctHelper.GetModelSpace(database, transaction);

            var polyline = new Polyline();
            for (int index = 0; index < shape.Vertices.Length; index++)
            {
                Point3d vertex = shape.Vertices[index];
                polyline.AddVertexAt(index, new Point2d(vertex.X, vertex.Y), shape.Bulges[index], 0, 0);
            }

            polyline.Closed = true;
            polyline.Elevation = shape.Vertices[0].Z;
            polyline.Layer = layer;
            polyline.TransformBy(ucsToWcs);
            modelSpace.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);

            foreach (Point3d[] cutLine in shape.CutLines)
            {
                var line = new Line(cutLine[0], cutLine[1]) { Layer = layer };
                line.TransformBy(ucsToWcs);
                modelSpace.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
            }

            transaction.Commit();
        }

        private static bool TryGetLayout(
            double width,
            double offset,
            double length,
            out double angle,
            out double radius,
            out double straightLength)
        {
            angle = 0;
            radius = 0;
            straightLength = 0;

            double absoluteOffset = Math.Abs(offset);
            if (absoluteOffset < Tolerance || length < absoluteOffset) return false;

            double standardRadius = StandardRadiusRatio * width;
            double LayoutEquation(double value) =>
                length * Math.Sin(value) - absoluteOffset * Math.Cos(value)
                - 2.0 * standardRadius * (1.0 - Math.Cos(value));

            const double step = 0.05 * Math.PI / 180.0;
            double maximumAngle = 89.9 * Math.PI / 180.0;
            double previousValue = LayoutEquation(0);
            for (double candidate = step; candidate <= maximumAngle; candidate += step)
            {
                double currentValue = LayoutEquation(candidate);
                if (previousValue < 0 && currentValue >= 0)
                {
                    double lower = candidate - step;
                    double upper = candidate;
                    for (int iteration = 0; iteration < 60; iteration++)
                    {
                        double midpoint = (lower + upper) / 2.0;
                        if (LayoutEquation(midpoint) < 0) lower = midpoint; else upper = midpoint;
                    }

                    angle = upper;
                    radius = standardRadius;
                    straightLength = Math.Max(0,
                        length * Math.Cos(angle) + absoluteOffset * Math.Sin(angle)
                        - 2.0 * standardRadius * Math.Sin(angle));
                    return true;
                }

                previousValue = currentValue;
            }

            radius = (length * length + absoluteOffset * absoluteOffset) / (4.0 * absoluteOffset);
            if (radius < MinimumRadiusRatio * width - Tolerance) return false;

            angle = 2.0 * Math.Atan(absoluteOffset / length);
            return true;
        }
    }
}
