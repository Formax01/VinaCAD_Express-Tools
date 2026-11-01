using Teigha.Geometry;

namespace Tools.Model
{
    public enum DuctBranchType
    {
        EEX, EEY, EEZ
    }

    public class DuctBranchModel
    {
        public DuctBranchType Type { get; set; }

        public Point3d InsertPoint { get; set; }
        public Vector3d Direction { get; set; } = Vector3d.XAxis;
        public string LayerName { get; set; }

        public double MainWidth { get; set; }        // W
        public double MainHeight { get; set; }       // H
        public double ThroughWidth { get; set; }     // Ws  (eex, eez)
        public double ThroughHeight { get; set; }    // Hs  (eex, eez)
        public double BranchHeight { get; set; }     // Hb  (dùng chung các nhánh)

        public double Branch1Width { get; set; }     // Wb  (eez: nhánh trái)
        public double Branch1Radius { get; set; }    // R
        public double Branch2Width { get; set; }     // Wb2 (chỉ eez dùng riêng; eex/eey = nhánh 1)
        public double Branch2Radius { get; set; }    // R2

        public ElbowSide BranchSide { get; set; }    // chỉ eex: rẽ trái hay phải
    }
}