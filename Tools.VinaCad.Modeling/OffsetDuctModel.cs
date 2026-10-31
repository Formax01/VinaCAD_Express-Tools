using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class OffsetDuctModel
    {
        public Point3d StartPoint { get; set; }     
        public double Width1 { get; set; }          // chiều rộng ống 1
        public double Width2 { get; set; }          // chiều rộng ống 2
        public double Offset { get; set; }          // cao độ lượn, có dấu: + = trái, - = phải
        public double Length { get; set; }          // L
        public Vector3d Direction { get; set; } = Vector3d.XAxis;
        public string Layer { get; set; } = "ONG_GIO";
    }
}
