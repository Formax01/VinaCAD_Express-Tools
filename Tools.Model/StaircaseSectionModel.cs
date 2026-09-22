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
        public bool JoinGroupAfterDraw { get; set; }

        public double CurrentStepHeight => StepNumber > 0 ? StoreyHeight / StepNumber : 0.0;

        public StaircaseSectionModel Copy()
        {
            return (StaircaseSectionModel)MemberwiseClone();
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
        public StaircaseSegment( StaircasePoint start, StaircasePoint end, StaircaseSegmentStyle style = StaircaseSegmentStyle.Primary)
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
