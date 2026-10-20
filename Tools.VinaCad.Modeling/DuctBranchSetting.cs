using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public class DuctBranchPreset
    {
        public double MainWidth;        // W
        public double MainHeight;       // H
        public double ThroughWidth;     // Ws
        public double ThroughHeight;    // Hs
        public double BranchWidth;      // Wb  (nhánh 1)
        public double Radius;           // R   (nhánh 1, đường tim)
        public double Branch2Width;     // Wb2 (chỉ eez)
        public double Radius2;          // R2  (chỉ eez)
        public double BranchHeight;     // Hb
        public ElbowSide Side;          // hướng rẽ (chỉ eex)
    }

    public static class DuctBranchSettings
    {
        public const string DefaultLayer = "HVAC-DUCT";
        public const string GroupPrefix = "DUCTBRANCH_";

        public static readonly DuctBranchPreset Eex = new DuctBranchPreset
        {
            MainWidth = 400,
            MainHeight = 40,
            ThroughWidth = 300,
            ThroughHeight = 140,
            BranchWidth = 300,
            Radius = 280,
            BranchHeight = 20,
            Side = ElbowSide.Right
        };

        public static readonly DuctBranchPreset Eey = new DuctBranchPreset
        {
            MainWidth = 400,
            MainHeight = 0,
            BranchWidth = 400,
            Radius = 380,
            BranchHeight = 0
        };

        public static readonly DuctBranchPreset Eez = new DuctBranchPreset
        {
            MainWidth = 400,
            MainHeight = 0,
            ThroughWidth = 240,
            ThroughHeight = 100,
            BranchWidth = 320,
            Radius = 320,
            Branch2Width = 320,
            Radius2 = 320,
            BranchHeight = 40
        };

        public static DuctBranchPreset PresetFor(DuctBranchType type)
        {
            if (type == DuctBranchType.EEX) return Eex;
            if (type == DuctBranchType.EEY) return Eey;
            return Eez;
        }
    }
}