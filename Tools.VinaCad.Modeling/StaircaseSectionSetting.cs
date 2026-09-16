using System;
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


        public static StaircaseSectionType Type { get; set; } = DefaultType;
        public static bool FirstRunRightward { get; set; } = DefaultFirstRunRightward;
        public static string LayerName { get; set; } = DefaultLayerName;
        public static string SecondaryLayerName { get; set; } = DefaultSecondaryLayerName;
        public static int StoreyNumber { get; set; } = DefaultStoreyNumber;
        public static double StoreyHeight { get; set; } = DefaultStoreyHeightMillimetres;
        public static double TreadRun { get; set; } = DefaultTreadRunMillimetres;
        public static int StepNumber { get; set; } = DefaultStepNumber;
        public static int FirstFlightStepNumber { get; set; } = DefaultFirstFlightStepNumber;
        public static double Landing1Width { get; set; } = DefaultLanding1WidthMillimetres;
        public static double Landing2Width { get; set; } = DefaultLanding2WidthMillimetres;
        public static double GirderHeight { get; set; } = DefaultGirderHeightMillimetres;
        public static double GirderWidth { get; set; } = DefaultGirderWidthMillimetres;
        public static double BeamHeight { get; set; } = DefaultBeamHeightMillimetres;
        public static double BeamWidth { get; set; } = DefaultBeamWidthMillimetres;
        public static bool HasBeam1 { get; set; } = DefaultHasBeam1;
        public static bool HasBeam2 { get; set; } = DefaultHasBeam2;
        public static double BoardThickness { get; set; } = DefaultBoardThicknessMillimetres;
        public static double RailingHeight { get; set; } = DefaultRailingHeightMillimetres;
        public static bool CreateGroup { get; set; } = DefaultCreateGroup;

        public static bool FirstRunLeftward => !FirstRunRightward;
        public static double CurrentStepHeight =>
            StepNumber > 0 ? StoreyHeight / StepNumber : 0.0;

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

        public static void LoadFrom(StaircaseSectionModel settings)
        {

            ArgumentNullException.ThrowIfNull(settings);

            Type = settings.Type;
            FirstRunRightward = settings.FirstRunRightward;
            StoreyNumber = settings.StoreyNumber;
            StoreyHeight = settings.StoreyHeight;
            TreadRun = settings.TreadRun;
            StepNumber = settings.StepNumber;
            FirstFlightStepNumber = settings.FirstFlightStepNumber;
            Landing1Width = settings.Landing1Width;
            Landing2Width = settings.Landing2Width;
            GirderHeight = settings.GirderHeight;
            GirderWidth = settings.GirderWidth;
            BeamHeight = settings.BeamHeight;
            BeamWidth = settings.BeamWidth;
            HasBeam1 = settings.HasBeam1;
            HasBeam2 = settings.HasBeam2;
            BoardThickness = settings.BoardThickness;
            RailingHeight = settings.RailingHeight;
            CreateGroup = settings.CreateGroup;
        }

        public static StaircaseSectionModel ToModel()
        {

            var settings = new StaircaseSectionModel
            {
                Type = Type,
                FirstRunRightward = FirstRunRightward,
                StoreyNumber = StoreyNumber,
                StoreyHeight = StoreyHeight,
                TreadRun = TreadRun,
                StepNumber = StepNumber,
                FirstFlightStepNumber = FirstFlightStepNumber,
                Landing1Width = Landing1Width,
                Landing2Width = Landing2Width,
                GirderHeight = GirderHeight,
                GirderWidth = GirderWidth,
                BeamHeight = BeamHeight,
                BeamWidth = BeamWidth,
                HasBeam1 = HasBeam1,
                HasBeam2 = HasBeam2,
                BoardThickness = BoardThickness,
                RailingHeight = RailingHeight,
                CreateGroup = CreateGroup
            };

            return settings;
        }
    }
}
