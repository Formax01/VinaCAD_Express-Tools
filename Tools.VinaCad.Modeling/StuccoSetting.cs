namespace Tools.VinaCad.Modeling
{
    public sealed class StuccoSetting
    {
        public const string DefaultLayerName = "FN";
        public const short DefaultLayerColorIndex = 31;
        public const double DefaultThickness = 30.0;

        public string LayerName { get; set; } = DefaultLayerName;
        public short LayerColorIndex { get; set; } = DefaultLayerColorIndex;
        public double Thickness { get; set; } = DefaultThickness;

        public StuccoSetting Clone()
        {
            return new StuccoSetting
            {
                LayerName = LayerName,
                LayerColorIndex = LayerColorIndex,
                Thickness = Thickness
            };
        }
    }

    public enum StuccoSettingRequest
    {
        None = 0,
        Accept = 1,
        PickLayer = 2,
        MeasureThickness = 3
    }
}
