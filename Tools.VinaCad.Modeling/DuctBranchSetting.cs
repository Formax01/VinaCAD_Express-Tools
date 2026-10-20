using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public class DuctBranchPreset
    {
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
        public const double DefaultMainWidth = 400;
        public const string DefaultLayer = "HVAC-DUCT";
        public const string GroupPrefix = "DUCTBRANCH_";

        public static readonly DuctBranchPreset Eex = new DuctBranchPreset
        {
            MainHeight = 0.10,
            ThroughWidth = 0.75,
            ThroughHeight = 0.35,
            BranchWidth = 0.75,
            Radius = 0.70,
            BranchHeight = 0.05,
            Side = ElbowSide.Right
        };

        public static readonly DuctBranchPreset Eey = new DuctBranchPreset
        {
            MainHeight = 0,
            BranchWidth = 1.00,
            Radius = 0.95,
            BranchHeight = 0
        };

        public static readonly DuctBranchPreset Eez = new DuctBranchPreset
        {
            MainHeight = 0,
            ThroughWidth = 0.60,
            ThroughHeight = 0.25,
            BranchWidth = 0.80,
            Radius = 0.80,
            Branch2Width = 0.80,
            Radius2 = 0.80,
            BranchHeight = 0.10
        };

        public static DuctBranchPreset PresetFor(DuctBranchType type)
        {
            if (type == DuctBranchType.EEX) return Eex;
            if (type == DuctBranchType.EEY) return Eey;
            return Eez;
        }
    }
}