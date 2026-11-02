using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public class ShoeModel
    {
        public Point3d StartPoint { get; set; }
        public double Height { get; set; }      // chiều cao (cạnh thẳng đứng)
        public double SmallSide { get; set; }   // cạnh bé
        public double BaseSide { get; set; }    // cạnh đáy
        public Vector3d Direction { get; set; } = Vector3d.XAxis;
        public bool Mirror { get; set; }
        public string Layer { get; set; } = "ONG_GIO";
    }
}