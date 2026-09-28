using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Tools.Model
{
    public enum GridAxisLabelType
    {
        Number,
        Letter
    }

    public sealed class GridAxisInput
    {
        public IReadOnlyList<double> Breadths { get; }
        public IReadOnlyList<double> Depths { get; }
        public bool DrawAnnotations { get; }
        public GridAxisLabelType VerticalAxisLabelType { get; }
        public string VerticalAxisStart { get; }
        public GridAxisLabelType HorizontalAxisLabelType { get; }
        public string HorizontalAxisStart { get; }

        public double TotalWidth => Breadths.Sum();
        public double TotalDepth => Depths.Sum();

        public GridAxisInput(
            IEnumerable<double> breadths,
            IEnumerable<double> depths,
            bool drawAnnotations,
            GridAxisLabelType verticalAxisLabelType = GridAxisLabelType.Number,
            string verticalAxisStart = "1",
            GridAxisLabelType horizontalAxisLabelType = GridAxisLabelType.Letter,
            string horizontalAxisStart = "A")
        {
            Breadths = new ReadOnlyCollection<double>(breadths.ToList());
            Depths = new ReadOnlyCollection<double>(depths.ToList());
            DrawAnnotations = drawAnnotations;
            VerticalAxisLabelType = verticalAxisLabelType;
            VerticalAxisStart = verticalAxisStart;
            HorizontalAxisLabelType = horizontalAxisLabelType;
            HorizontalAxisStart = horizontalAxisStart;
        }
    }

}
