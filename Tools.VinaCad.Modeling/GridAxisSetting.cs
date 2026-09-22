namespace Tools.VinaCad.Modeling
{
    public static class GridAxisSetting
    {
        public static string DefaultBreadths { get; set; } = "4000 2000 4000 6000";
        public static string DefaultDepths { get; set; } = "5000";
        public static bool DefaultDrawAnnotations { get; set; } = true;

        public static double MinimumAnnotationTextHeight { get; set; } = 2.5;
        public static double AnnotationTextHeightToBayRatio { get; set; } = 0.05;
        public static double MaximumAnnotationTextHeightToGridRatio { get; set; } = 0.12;

        public static double InnerDimensionOffsetToBayRatio { get; set; } = 0.20;
        public static double DimensionLineGapToBayRatio { get; set; } = 0.15;
        public static double BubbleGapToBayRatio { get; set; } = 0.18;
        public static double AxisToDimensionGapToBayRatio { get; set; } = 0.05;
    }
}
