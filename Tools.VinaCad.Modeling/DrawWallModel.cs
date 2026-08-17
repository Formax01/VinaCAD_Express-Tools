namespace Tools.VinaCad.Modeling
{
    public class DrawWallModel
    {
        public double Thickness { get; set; }
        public WallAlignment Alignment { get; set; }
        public string WallLayer { get; set; }
        public DrawWallModel()
        {
            Thickness = 200;
            Alignment = WallAlignment.Center;
            WallLayer = "Wall";
        }
    }

    public enum WallAlignment
    {
        Center = 0,  // Centerline alignment - offset equally on both sides
        Left = 1,    // Left/outside alignment - offset to the right
        Right = 2    // Right/inside alignment - offset to the left
    }
}
