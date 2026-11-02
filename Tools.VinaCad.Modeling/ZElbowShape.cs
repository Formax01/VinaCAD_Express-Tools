using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class ZElbowShape
    {
        public Point3d[] Vertices { get; set; }
        public double[] Bulges { get; set; }          // bulge của đoạn bắt đầu tại đỉnh i
        public Point3d[][] CutLines { get; set; }     // mỗi phần tử gồm 2 điểm
        public double Theta { get; set; }
        public double Radius { get; set; }
        public double Straight { get; set; }          // chiều dài ống thẳng ở giữa (0 nếu không có)
    }
}
