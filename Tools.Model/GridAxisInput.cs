using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Tools.Model
{
    public sealed class GridAxisInput
    {
        public IReadOnlyList<double> Breadths { get; }
        public IReadOnlyList<double> Depths { get; }
        public bool DrawAnnotations { get; }

        public double TotalWidth => Breadths.Sum();
        public double TotalDepth => Depths.Sum();

        public GridAxisInput(IEnumerable<double> breadths,IEnumerable<double> depths,bool drawAnnotations)
        {
            Breadths = new ReadOnlyCollection<double>(breadths.ToList());
            Depths = new ReadOnlyCollection<double>(depths.ToList());
            DrawAnnotations = drawAnnotations;
        }
    }

}
