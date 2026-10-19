using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public static class DuctBranchSettings
    {
        public const double DefaultMainWidth = 400;
        public const double DefaultMainHeight = 200;
        public const double DefaultThroughWidth = 400;
        public const double DefaultThroughHeight = 100;
        public const double DefaultBranchWidth = 200;
        public const double DefaultBranchHeight = 200;
        public const double DefaultRadiusRatio = 1.0;
        public const RadiusMode DefaultRadiusMode = RadiusMode.Center;
        public const ElbowSide DefaultSide = ElbowSide.Left;

        public const double BranchAngleDeg = 90.0;               // góc rẽ nhánh cong (cố định 90)

        public const string DefaultLayer = "HVAC-DUCT";
        public const string GroupPrefix = "DUCTBRANCH_";
    }
}