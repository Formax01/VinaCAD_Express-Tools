using Teigha.Geometry;

namespace Tools.Model
{
    public static class StuccoModel
    {
        public readonly struct LineSegment
        {
            public LineSegment(Point3d start, Point3d end)
            {
                Start = start;
                End = end;
            }

            public Point3d Start { get; }
            public Point3d End { get; }
        }

        public readonly struct WallStrip
        {
            public WallStrip(Point3d origin, Vector3d direction, Vector3d normal, double minimumAlong, double maximumAlong, double minimumAcross, double maximumAcross)
            {
                Origin = origin;
                Direction = direction;
                Normal = normal;
                MinimumAlong = minimumAlong;
                MaximumAlong = maximumAlong;
                MinimumAcross = minimumAcross;
                MaximumAcross = maximumAcross;
            }

            public Point3d Origin { get; }
            public Vector3d Direction { get; }
            public Vector3d Normal { get; }
            public double MinimumAlong { get; }
            public double MaximumAlong { get; }
            public double MinimumAcross { get; }
            public double MaximumAcross { get; }
            public Point3d Center => Origin + Direction * ((MinimumAlong + MaximumAlong) * 0.5) + Normal * ((MinimumAcross + MaximumAcross) * 0.5);
            public double Length => MaximumAlong - MinimumAlong;
            public double Width => MaximumAcross - MinimumAcross;
        }
    }
}
