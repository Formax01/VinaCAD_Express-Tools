using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public static class StaircaseSectionSetting
    {

        public const StaircaseSectionType DefaultType = StaircaseSectionType.DoubleFlight;
        public const bool DefaultFirstRunRightward = true;
        public const string DefaultLayerName = "Slice_Stair";
        public const string DefaultSecondaryLayerName = "Slice_Stair_Secondary";

        public const int DefaultStoreyNumber = 5;
        public const int DefaultStepNumber = 18;
        public const int DefaultFirstFlightStepNumber = 9;
        public const double DefaultStoreyHeightMillimetres = 2800.0;
        public const double DefaultTreadRunMillimetres = 280.0;
        public const double DefaultBoardThicknessMillimetres = 100.0;
        public const double DefaultRailingHeightMillimetres = 900.0;


        public const double DefaultLanding1WidthMillimetres = 1400.0;
        public const double DefaultLanding2WidthMillimetres = 1400.0;
        public const double DefaultGirderHeightMillimetres = 450.0;
        public const double DefaultGirderWidthMillimetres = 200.0;
        public const double DefaultBeamHeightMillimetres = 350.0;
        public const double DefaultBeamWidthMillimetres = 200.0;
        public const bool DefaultHasBeam1 = true;
        public const bool DefaultHasBeam2 = true;
        public const bool DefaultCreateGroup = false;


        public static string LayerName { get; set; } = DefaultLayerName;
        public static string SecondaryLayerName { get; set; } = DefaultSecondaryLayerName;
        public static StaircaseSectionModel CreateInitial()
        {
            // Tất cả chiều dài của command LTP được dùng trực tiếp theo milimét.
            var settings = new StaircaseSectionModel
            {
                Type = DefaultType,
                FirstRunRightward = DefaultFirstRunRightward,
                StoreyNumber = DefaultStoreyNumber,
                StoreyHeight = DefaultStoreyHeightMillimetres,
                TreadRun = DefaultTreadRunMillimetres,
                StepNumber = DefaultStepNumber,
                FirstFlightStepNumber = DefaultFirstFlightStepNumber,
                Landing1Width = DefaultLanding1WidthMillimetres,
                Landing2Width = DefaultLanding2WidthMillimetres,
                GirderHeight = DefaultGirderHeightMillimetres,
                GirderWidth = DefaultGirderWidthMillimetres,
                BeamHeight = DefaultBeamHeightMillimetres,
                BeamWidth = DefaultBeamWidthMillimetres,
                HasBeam1 = DefaultHasBeam1,
                HasBeam2 = DefaultHasBeam2,
                BoardThickness = DefaultBoardThicknessMillimetres,
                RailingHeight = DefaultRailingHeightMillimetres,
                CreateGroup = DefaultCreateGroup
            };

            return settings;
        }
    }
}
