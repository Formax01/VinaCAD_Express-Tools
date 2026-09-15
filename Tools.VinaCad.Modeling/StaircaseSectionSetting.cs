using System;
using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public static class StaircaseSectionSetting
    {
        public const StaircaseSectionType DefaultType = StaircaseSectionType.DoubleFlight;
        public const bool DefaultFirstRunRightward = true;
        public const int DefaultStoreyNumber = 5;
        public const int DefaultStepNumber = 18;
        public const int DefaultFirstFlightStepNumber = 9;
        public const bool DefaultHasBeam1 = true;
        public const bool DefaultHasBeam2 = true;
        public const bool DefaultCreateGroup = false;

        public const double DefaultStoreyHeightMillimetres = 2800.0;
        public const double DefaultTreadRunMillimetres = 280.0;
        public const double DefaultLanding1WidthMillimetres = 1400.0;
        public const double DefaultLanding2WidthMillimetres = 1400.0;
        public const double DefaultGirderHeightMillimetres = 450.0;
        public const double DefaultGirderWidthMillimetres = 200.0;
        public const double DefaultBeamHeightMillimetres = 350.0;
        public const double DefaultBeamWidthMillimetres = 200.0;
        public const double DefaultBoardThicknessMillimetres = 100.0;
        public const double DefaultRailingHeightMillimetres = 900.0;

        public static StaircaseSectionModel CreateDefault(double drawingUnitsPerMillimetre)
        {
            // B1: Kiểm tra hệ số đơn vị trước khi quy đổi kích thước.
            if (!double.IsFinite(drawingUnitsPerMillimetre) || drawingUnitsPerMillimetre <= 0)
                throw new ArgumentOutOfRangeException(nameof(drawingUnitsPerMillimetre));

            // B2: Khởi tạo đầy đủ biến cài đặt LTP trong đơn vị bản vẽ hiện tại.
            var settings = new StaircaseSectionModel
            {
                Type = DefaultType,
                FirstRunRightward = DefaultFirstRunRightward,
                StoreyNumber = DefaultStoreyNumber,
                StoreyHeight = DefaultStoreyHeightMillimetres * drawingUnitsPerMillimetre,
                TreadRun = DefaultTreadRunMillimetres * drawingUnitsPerMillimetre,
                StepNumber = DefaultStepNumber,
                FirstFlightStepNumber = DefaultFirstFlightStepNumber,
                Landing1Width = DefaultLanding1WidthMillimetres * drawingUnitsPerMillimetre,
                Landing2Width = DefaultLanding2WidthMillimetres * drawingUnitsPerMillimetre,
                GirderHeight = DefaultGirderHeightMillimetres * drawingUnitsPerMillimetre,
                GirderWidth = DefaultGirderWidthMillimetres * drawingUnitsPerMillimetre,
                BeamHeight = DefaultBeamHeightMillimetres * drawingUnitsPerMillimetre,
                BeamWidth = DefaultBeamWidthMillimetres * drawingUnitsPerMillimetre,
                HasBeam1 = DefaultHasBeam1,
                HasBeam2 = DefaultHasBeam2,
                BoardThickness = DefaultBoardThicknessMillimetres * drawingUnitsPerMillimetre,
                RailingHeight = DefaultRailingHeightMillimetres * drawingUnitsPerMillimetre,
                CreateGroup = DefaultCreateGroup
            };

            // B3: Action và Reset dùng chung bộ giá trị mặc định này.
            return settings;
        }
    }
}
