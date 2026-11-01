using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class SquareToRoundModel
    {
        public Point3d StartPoint { get; set; }      // tâm đầu lớn
        public double BigSize { get; set; }          // kích thước đầu lớn
        public double SmallSize { get; set; }        // kích thước đầu nhỏ
        public double Length { get; set; }           // L
        public Vector3d Direction { get; set; } = Vector3d.XAxis;
        public string Layer { get; set; } = "ONG_GIO";
    }
}
