using System;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper.Helper
{
    /// <summary>
    /// Tinh toan hinh hoc ong gio luon (offset duct).
    /// </summary>
    public static class OffsetDuctHelper
    {
        private static readonly double MinimumAngle = 30.0 * Math.PI / 180.0;
        private static readonly double MaximumAngle = 60.0 * Math.PI / 180.0;
        private static readonly double AngleStep = 0.25 * Math.PI / 180.0;
        private const double MinimumStubRatio = 0.12;

        public static double GetMinimumLength(double firstWidth, double secondWidth, double offset)
        {
            double minimumStub = MinimumStubRatio * Math.Min(firstWidth, secondWidth);
            double shortestLength = double.MaxValue;

            for (double angle = MinimumAngle; angle <= MaximumAngle + 1e-12; angle += AngleStep)
            {
                shortestLength = Math.Min(
                    shortestLength,
                    GetRequiredLength(firstWidth, secondWidth, offset, angle, minimumStub));
            }

            return shortestLength;
        }

        public static Point3d[] GetPoints(
            Point3d start,
            Vector3d direction,
            double firstWidth,
            double secondWidth,
            double offset,
            double length)
        {
            Vector3d along = direction.GetNormal();
            Vector3d left = along.RotateBy(Math.PI / 2, Vector3d.ZAxis);
            Point3d center = start - left * (firstWidth / 2.0);
            double angle = GetAngle(firstWidth, secondWidth, offset, length);

            Point2d[] leftWall = GetWallPoints(1, firstWidth, secondWidth, offset, length, angle);
            Point2d[] rightWall = GetWallPoints(-1, firstWidth, secondWidth, offset, length, angle);
            var points = new Point3d[8];

            for (int index = 0; index < 4; index++)
            {
                points[index] = center + along * leftWall[index].X + left * leftWall[index].Y;
                points[4 + index] = center + along * rightWall[3 - index].X + left * rightWall[3 - index].Y;
            }

            return points;
        }

        private static double GetAngle(double firstWidth, double secondWidth, double offset, double length)
        {
            double minimumStub = MinimumStubRatio * Math.Min(firstWidth, secondWidth);
            for (double angle = MinimumAngle; angle <= MaximumAngle + 1e-12; angle += AngleStep)
            {
                if (GetRequiredLength(firstWidth, secondWidth, offset, angle, minimumStub) <= length + 1e-9)
                    return angle;
            }

            return MaximumAngle;
        }

        private static double GetRequiredLength(
            double firstWidth,
            double secondWidth,
            double offset,
            double angle,
            double minimumStub)
        {
            double diagonalLength = Math.Abs(offset) / Math.Tan(angle);
            double overflow = 0;

            foreach (int side in new[] { 1, -1 })
            {
                Point2d[] wall = GetWallPoints(side, firstWidth, secondWidth, offset, diagonalLength, angle);
                overflow = Math.Max(overflow, -wall[1].X);
                overflow = Math.Max(overflow, wall[2].X - diagonalLength);
            }

            return diagonalLength + 2.0 * (overflow + minimumStub);
        }

        /// <summary>
        /// Tra ve cac diem dau vao, goc thu nhat, goc thu hai va dau ra cua mot thanh ong trong he toa do cuc bo.
        /// </summary>
        private static Point2d[] GetWallPoints(
            int side,
            double firstWidth,
            double secondWidth,
            double offset,
            double length,
            double angle)
        {
            int offsetSign = offset >= 0 ? 1 : -1;
            double signedAngle = offsetSign * angle;
            double directionX = Math.Cos(signedAngle);
            double directionY = Math.Sin(signedAngle);
            double normalX = -directionY;
            double normalY = directionX;
            double diagonalLength = Math.Abs(offset) / Math.Tan(angle);
            double diagonalStart = (length - diagonalLength) / 2.0;
            double diagonalWidth = (firstWidth + secondWidth) / 2.0;

            double pointX = diagonalStart + side * (diagonalWidth / 2.0) * normalX;
            double pointY = side * (diagonalWidth / 2.0) * normalY;
            double startY = side * firstWidth / 2.0;
            double endY = offset + side * secondWidth / 2.0;
            double firstDistance = (startY - pointY) / directionY;
            double secondDistance = (endY - pointY) / directionY;

            return new[]
            {
                new Point2d(0, startY),
                new Point2d(pointX + firstDistance * directionX, startY),
                new Point2d(pointX + secondDistance * directionX, endY),
                new Point2d(length, endY)
            };
        }
    }
}
