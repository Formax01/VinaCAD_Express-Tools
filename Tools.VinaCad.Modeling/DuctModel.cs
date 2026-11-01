using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class DuctModel
    {
        public bool IsEdgeMode { get; set; }   
        public int Side { get; set; } = 1;     
        public Point3d StartPoint { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
        public Vector3d Direction { get; set; }   // vector đơn vị
        public string Layer { get; set; } = "ONG_GIO";
    }
}
