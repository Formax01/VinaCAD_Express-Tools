using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.Model
{
    public enum DuctElbowType 
    { 
        Equal, 
        Reducing, 
        Vertical 
    }
    public enum ElbowSide 
    { 
        Left, 
        Right 
    }
    public enum VerticalDirection 
    { 
        Down, 
        Up 
    }
    public enum RadiusMode 
    { 
        Center, 
        Inner 
    }

    public class DuctElbowModel
    {
        public DuctElbowType Type { get; set; }
        public Point3d InsertPoint { get; set; }
        public Vector3d Direction { get; set; } = Vector3d.XAxis;   
        public double Width1 { get; set; }   
        public double Width2 { get; set; }
        public double InnerRadius { get; set; }
        public double CenterRadius { get; set; }
        public double AngleDeg { get; set; }
        public ElbowSide Side { get; set; }

        public VerticalDirection VerticalDir { get; set; }
        public double DropLength { get; set; }

        public string LayerName { get; set; }
        public bool ShowCenterLine { get; set; }
        public bool ShowLabel { get; set; }
    }
}
