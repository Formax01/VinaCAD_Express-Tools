using System.Collections.Generic;

namespace Tools.Model
{
    public sealed class WindowStyleModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AssetPath { get; set; } = string.Empty;
        public List<WindowPreviewPrimitive> PreviewGeometry { get; set; } = new List<WindowPreviewPrimitive>();
        public double OpeningMinimumX { get; set; }
        public double OpeningMaximumX { get; set; }
        public double DefaultWidth { get; set; } = 1200;
        public double DefaultHeight { get; set; } = 1500;
    }

    public enum WindowPreviewPrimitiveKind
    {
        Line,
        Arc,
        Circle
    }

    public sealed class WindowPreviewPrimitive
    {
        public WindowPreviewPrimitiveKind Kind { get; set; }
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

    public sealed class WindowStyleCatalog
    {
        public int SchemaVersion { get; set; } = 1;
        public string Units { get; set; } = "Millimeters";
        public double DefaultWallThickness { get; set; } = 200;
        public List<WindowStyleModel> Styles { get; set; } = new List<WindowStyleModel>();
    }

    public sealed class WindowStyleSelection
    {
        public WindowStyleModel Style { get; set; } = new WindowStyleModel();
        public double Width { get; set; }
        public double Height { get; set; }
        public double WallThickness { get; set; }
        public bool UseEdgeDistance { get; set; }
        public double EdgeDistance { get; set; }
        public bool PlaceAtWallCenter { get; set; }
        public bool ReverseAlongWall { get; set; }
        public bool MirrorAcrossWall { get; set; }
    }
}
