using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.Model
{
    public enum DuctBranchType
    {
        EEX, EEY, EEZ
    }

    public class DuctBranchModel
    {
        public DuctBranchType Type { get; set; }

        public Point3d InsertPoint { get; set; }
        public Vector3d Direction { get; set; } = Vector3d.XAxis;

        public double MainWidth { get; set; }           
        public double MainHeight { get; set; }           
        public double ThroughWidth { get; set; }    
        public double ThroughHeight { get; set; }        
        public double BranchHeight { get; set; }        

        public double Branch1Width { get; set; }      
        public double Branch1CenterRadius { get; set; }  
        public double Branch2Width { get; set; }       
        public double Branch2CenterRadius { get; set; } 

        public ElbowSide BranchSide { get; set; }    

        public string LayerName { get; set; }
    }
}