using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public enum BbzSide
    {
        Left,
        Right
    }

    /// <summary>
    /// State của một lần chạy lệnh Bbz (ống gió chữ Z 3D).
    /// Toạ độ StartPoint là toạ độ UCS hiện hành (đúng như Editor trả về).
    /// </summary>
    public class BbzModel
    {
        /// <summary>Điểm trên đầu ống gió 1 (cũng là tâm xoay), toạ độ UCS.</summary>
        public Point3d StartPoint { get; set; }

        /// <summary>Bề rộng ống gió 1 (đầu vào).</summary>
        public double Width1 { get; set; }

        /// <summary>Bề rộng ống gió 2 (đầu ra). Enter = bằng Width1.</summary>
        public double Width2 { get; set; }

        /// <summary>Cao độ lượn: khoảng cách giữa trục ống 1 và trục ống 2 (theo phương vuông góc trục).</summary>
        public double Offset { get; set; }

        /// <summary>Chiều dài L của chi tiết (dọc trục).</summary>
        public double Length { get; set; }

        /// <summary>Chiều cao ống gió (phương Z) - phần mới so với BZ.</summary>
        public double Height { get; set; }

        public BbzSide Side { get; set; } = BbzSide.Left;
    }
}