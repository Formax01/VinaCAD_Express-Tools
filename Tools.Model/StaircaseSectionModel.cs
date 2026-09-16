using System;

namespace Tools.Model
{
    public enum StaircaseSectionType
    {
        DoubleFlight,
        SingleFlight,
        Scissor
    }

    public sealed class StaircaseSectionModel
    {
        private const string InvalidPlanDimensionsMessage =
            "Chiều cao tầng, chiều rộng bậc phải lớn hơn 0; chiều rộng chiếu nghỉ không được âm.";
        private const string InvalidCountsMessage =
            "Số tầng phải từ 1 và tổng số bậc mỗi tầng phải từ 2.";
        private const string InvalidFirstFlightMessage =
            "Số bậc vế đầu phải nằm trong khoảng từ 1 đến nhỏ hơn tổng số bậc.";
        private const string InvalidStructureDimensionsMessage =
            "Kích thước kết cấu không được âm và chiều dày bản phải lớn hơn 0.";
        private const string InvalidBeamDimensionsMessage =
            "Chiều cao và chiều rộng dầm phụ phải lớn hơn 0 khi bật Dầm 1 hoặc Dầm 2.";
        private const string InvalidStaircaseTypeMessage = "Kiểu cầu thang không hợp lệ.";

        public StaircaseSectionType Type { get; set; }
        public bool FirstRunRightward { get; set; }
        public int StoreyNumber { get; set; }
        public double StoreyHeight { get; set; }
        public double TreadRun { get; set; }
        public int StepNumber { get; set; }
        public int FirstFlightStepNumber { get; set; }
        public double Landing1Width { get; set; }
        public double Landing2Width { get; set; }
        public double GirderHeight { get; set; }
        public double GirderWidth { get; set; }
        public double BeamHeight { get; set; }
        public double BeamWidth { get; set; }
        public bool HasBeam1 { get; set; }
        public bool HasBeam2 { get; set; }
        public double BoardThickness { get; set; }
        public double RailingHeight { get; set; }
        public bool CreateGroup { get; set; }

        public double CurrentStepHeight => StepNumber > 0 ? StoreyHeight / StepNumber : 0.0;

        public StaircaseSectionModel Copy()
        {
            return (StaircaseSectionModel)MemberwiseClone();
        }

        public bool TryValidate(out string message)
        {
            if (!Enum.IsDefined(Type)) return Fail(InvalidStaircaseTypeMessage, out message);
            if (!HasValidPlanDimensions()) return Fail(InvalidPlanDimensionsMessage, out message);
            if (!HasValidCounts()) return Fail(InvalidCountsMessage, out message);
            if (!HasValidFirstFlight()) return Fail(InvalidFirstFlightMessage, out message);
            if (!HasValidStructureDimensions()) return Fail(InvalidStructureDimensionsMessage, out message);
            if (!HasValidEnabledBeams()) return Fail(InvalidBeamDimensionsMessage, out message);

            message = string.Empty;
            return true;
        }

        private bool HasValidPlanDimensions()
        {
            return StoreyHeight > 0 && TreadRun > 0  && Landing1Width >= 0  && Landing2Width >= 0;
        }

        private bool HasValidCounts()
        {
            return StoreyNumber >= 1 && StepNumber >= 2;
        }

        private bool HasValidFirstFlight()
        {
            return Type == StaircaseSectionType.SingleFlight
                || FirstFlightStepNumber >= 1 && FirstFlightStepNumber < StepNumber;
        }

        private bool HasValidStructureDimensions()
        {
            return BoardThickness > 0  && RailingHeight >= 0  && GirderHeight >= 0 && GirderWidth >= 0 && BeamHeight >= 0 && BeamWidth >= 0;
        }

        private bool HasValidEnabledBeams()
        {
            return !HasBeam1 && !HasBeam2 || BeamHeight > 0 && BeamWidth > 0;
        }

        private static bool Fail(string validationMessage, out string message)
        {
            message = validationMessage;
            return false;
        }
    }

    public readonly struct StaircasePoint
    {
        public StaircasePoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    public enum StaircaseSegmentStyle
    {
        Primary,
        Secondary
    }

    public readonly struct StaircaseSegment
    {
        public StaircaseSegment(
            StaircasePoint start,
            StaircasePoint end,
            StaircaseSegmentStyle style = StaircaseSegmentStyle.Primary)
        {
            Start = start;
            End = end;
            Style = style;
        }

        public StaircasePoint Start { get; }
        public StaircasePoint End { get; }
        public StaircaseSegmentStyle Style { get; }
    }
}
