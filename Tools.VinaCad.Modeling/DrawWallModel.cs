using System;
using System.Collections.Generic;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class DrawWallModel
    {
        public double Thickness { get; set; }
        public WallAlignment Alignment { get; set; }
        public string WallLayer { get; set; }
        public List<WallSegment> Segments { get; set; }

        public DrawWallModel()
        {
            Thickness = 200;
            Alignment = WallAlignment.Center;
            WallLayer = "WALL";
            Segments = new List<WallSegment>();
        }
    }

    public enum WallAlignment
    {
        Center = 0,  // Centerline alignment - offset equally on both sides
        Left = 1,    // Left/outside alignment - offset to the right
        Right = 2    // Right/inside alignment - offset to the left
    }

    public class WallSegment
    {
        public Point3d StartPoint { get; set; }
        public Point3d EndPoint { get; set; }
        public Point3d Line1Start { get; set; }
        public Point3d Line1End { get; set; }
        public Point3d Line2Start { get; set; }
        public Point3d Line2End { get; set; }

        public WallSegment()
        {
            StartPoint = Point3d.Origin;
            EndPoint = Point3d.Origin;
            Line1Start = Point3d.Origin;
            Line1End = Point3d.Origin;
            Line2Start = Point3d.Origin;
            Line2End = Point3d.Origin;
        }
    }
}
