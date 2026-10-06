using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.Model
{
    public enum FlexDuctType
    {
       TopDown,
       Horizontal,
       DoubleTopDown,
    }
    public enum FlexDuctPathMode
    {
        Draw,
        Select
    }

    public class FlexDuctModel
    {
        public double Diameter { get; set; } = 250;
        public FlexDuctType DuctType { get; set; } = FlexDuctType.TopDown;
        public FlexDuctPathMode PathMode { get; set; } = FlexDuctPathMode.Draw;
    }
}
