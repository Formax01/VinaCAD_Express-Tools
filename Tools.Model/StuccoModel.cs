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
    }
}
