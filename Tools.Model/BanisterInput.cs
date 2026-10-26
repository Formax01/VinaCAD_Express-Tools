using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.Model
{
    public class BanisterInput
    {
        //public bool Sloped { get; set; } = false;
        public string LayerName { get; set; } = "LG";
        public short ColorIndex { get; set; } = 2;      // ACI 2 = vàng

        public double TotalHeight { get; set; } = 1050;
        public double HandrailDia { get; set; } = 60;
        public double HandrailExtend { get; set; } = 60;
        public double RailDia { get; set; } = 30;
        public double TopGap { get; set; } = 110;
        public double BottomGap { get; set; } = 110;
        public double ColumnDia { get; set; } = 30;
        public double ColumnGap { get; set; } = 1200;   // khoảng cách tối đa
        public bool HasSideColumns { get; set; } = true;
        public double PoleDia { get; set; } = 20;
        public double PoleGap { get; set; } = 110;      // khoảng hở tối đa
        public bool ToGroup { get; set; } = true;       // dùng ở GĐ 5

        //public double UnitScale { get; set; } = 1;  // mm -> m
        public string DrawingUnit { get; set; } = "Auto";   // Auto | mm | cm | m
    }
}
