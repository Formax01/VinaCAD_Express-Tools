using System.Collections.Generic;

namespace Tools.VinaCad.Helper.Helper
{
    /// <summary>Hình học lệnh OC2 (dùng lại OC1Segment, OC1Jig, OC1Helper.CreateLines).</summary>
    public static class OC2Helper
    {
        
        public static List<OC1Segment> BuildSegments(double size, double L)
        {
            double S = size;
            double D = S / 10;        // chiều cao hình thang = độ vát = Size/10
            double bottom = -(L - D); // đáy các đường đứng (tổng chiều cao từ đỉnh hình thang = L)

            List<OC1Segment> segments = new List<OC1Segment>
            {
                // hình thang đỏ
                new OC1Segment(0, 0, S, 0, OC1Kind.Red),          // cạnh đáy dài Size
                new OC1Segment(0, 0, D, D, OC1Kind.Red),          // cạnh xiên trái
                new OC1Segment(D, D, S - D, D, OC1Kind.Red),      // cạnh trên
                new OC1Segment(S - D, D, S, 0, OC1Kind.Red),      // cạnh xiên phải

                // 2 đường đỏ đứng
                new OC1Segment(0, 0, 0, bottom, OC1Kind.Red),
                new OC1Segment(S, 0, S, bottom, OC1Kind.Red),

                // 2 đường xám đứng, từ cạnh trên xuống đáy (dài L)
                new OC1Segment(D, D, D, bottom, OC1Kind.Gray),
                new OC1Segment(S - D, D, S - D, bottom, OC1Kind.Gray),
            };

            // Điểm chọn / tâm quay = trung điểm 2 đầu tự do (xa hình thang) của 2 thanh đỏ -> (S/2, bottom)
            double du = -S / 2;
            double dv = -bottom;

            foreach (OC1Segment seg in segments)
            {
                seg.U1 += du; seg.U2 += du;
                seg.V1 += dv; seg.V2 += dv;
            }

            return segments;
        }
    }
}