using System.Collections.Generic;

namespace Tools.Model
{
    public enum HingeSide
    {
        Left = 0,
        Right = 1
    }

    public enum OpeningDirection
    {
        Inside = 0,
        Outside = 1
    }

    public sealed class DoorStyleModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AssetPath { get; set; } = string.Empty;
        public List<DoorPreviewPrimitive> PreviewGeometry { get; set; } = new List<DoorPreviewPrimitive>();
        public double DefaultWidth { get; set; } = 900;
        public double DefaultHeight { get; set; } = 2200;
    }

    public enum DoorPreviewPrimitiveKind
    {
        Line,
        Arc,
        Circle
    }

    public sealed class DoorPreviewPrimitive
    {
        public DoorPreviewPrimitiveKind Kind { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }
        public double StartAngle { get; set; }
        public double EndAngle { get; set; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }

    public sealed class DoorStyleCatalog
    {
        public int SchemaVersion { get; set; } = 1;
        public string Units { get; set; } = "Millimeters";
        public double DefaultWallThickness { get; set; } = 200;
        public List<DoorStyleModel> Styles { get; set; } = new List<DoorStyleModel>();
    }

    public sealed class DoorStyleSelection
    {
        public DoorStyleModel Style { get; set; } = new DoorStyleModel();
        public double Width { get; set; }
        public double Height { get; set; }
        public double WallThickness { get; set; }
        public bool UseEdgeDistance { get; set; }
        public double EdgeDistance { get; set; }
        public bool PlaceAtWallCenter { get; set; }
        public bool ReverseAlongWall { get; set; }
        public bool MirrorAcrossWall { get; set; }
        public HingeSide HingeSide { get; set; } = HingeSide.Left;
        public OpeningDirection OpeningDirection { get; set; } = OpeningDirection.Inside;
    }
}
