using System;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper.Helper
{
    
    public static class ShoeHelper
    {
        public static double GetAutoBase(double height, double smallSide) => height + smallSide;

        public static Point3d[] GetCorners(
            Point3d start,
            Vector3d direction,
            double height,
            double smallSide,
            double baseSide,
            bool mirror)
        {
            Vector3d along = direction.GetNormal();
            Vector3d side = along.RotateBy(Math.PI / 2, Vector3d.ZAxis);
            if (mirror) side = side.Negate();

            Point3d smallStart = start - side * height;
            return new[]
            {
                start,
                smallStart,
                smallStart + along * smallSide,
                start + along * baseSide
            };
        }
    }
}
