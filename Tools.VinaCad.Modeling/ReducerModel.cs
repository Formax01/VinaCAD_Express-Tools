using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public enum ReducerSide { Center, Left, Right }

    public class ReducerModel
    {
        public Point3d StartPoint { get; set; }      // theo UCS hiện hành
        public double Length { get; set; }
        public double Width1 { get; set; }
        public double Width2 { get; set; }
        
        public Vector3d Direction { get; set; }      // vector đơn vị, theo UCS
        public ReducerSide Side { get; set; } = ReducerSide.Center;
        public string Layer { get; set; } = "ONG_GIO";
    }
}