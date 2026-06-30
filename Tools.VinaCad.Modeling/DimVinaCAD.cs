using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class DimVinaCAD
    {
        public Point3d Diem1 { get; set; }
        public Point3d Diem2 { get; set; }
        public Point3d DiemDung { get; set; }
        public double GocDim { get; set; }
        public ObjectId Id { get; set; }
        public Point3d DimLinePoint { get; set; }

        public double Rotation { get; set; }

        public double Dimlfac { get; set; } = 1.0;
        public double Value { get; set; }

        public double MinX => Math.Min(Diem1.X, Diem2.X);

        public double MaxX => Math.Max(Diem1.X, Diem2.X);

        public double MinY => Math.Min(Diem1.Y, Diem2.Y);

        public double MaxY => Math.Max(Diem1.Y, Diem2.Y);
    }
}

