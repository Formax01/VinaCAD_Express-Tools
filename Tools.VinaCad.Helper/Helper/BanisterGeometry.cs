using System;
using System.Collections.Generic;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    // Flat = true: chi tiết nằm ngang, Y là độ cao tuyệt đối (không theo dốc)
    public readonly record struct BanisterRect(double X, double Y, double W, double H, double Cap = 0);

    public static class BanisterGeometry
    {
        /// <param name="L">Chiều dài ngang (mm)</param>
        /// <param name="m">Độ dốc theo chiều đi (dy/dx), lan can ngang thì m = 0</param>
        public static List<BanisterRect> Build(BanisterInput i, double L, double m = 0.0)
        {
            var rects = new List<BanisterRect>();
            double k = Math.Sqrt(1 + m * m);     // = 1/cos(theta)
            double H = i.TotalHeight;

            double hd = i.HandrailDia * k;       // bề dày đứng của tay vịn
            double rd = i.RailDia * k;           // bề dày đứng của thanh ngang

            double handrailBottom = H - hd;
            double railTop = handrailBottom - i.TopGap;
            double railBottom = railTop - rd;                 // đáy thanh ngang trên
            double lowRailY = i.BottomGap;                    // đáy thanh ngang dưới
            double poleBottom = lowRailY + rd;                // song bắt đầu từ mặt trên thanh dưới

            // 1. Tay vịn
            if (Math.Abs(m) < 1e-9)
            {
                // Lan can ngang: một thanh dài, thò ra hai đầu
                rects.Add(new BanisterRect(-i.HandrailExtend, handrailBottom,
                                           L + 2 * i.HandrailExtend, hd));
            }
            else
            {
                // Lan can nghiêng: đoạn dốc ở giữa, hai đầu là đoạn nằm ngang dài Extend
                //rects.Add(new BanisterRect(0, handrailBottom, L, hd));
                //rects.Add(new BanisterRect(-i.HandrailExtend, handrailBottom,
                //                           i.HandrailExtend, hd, true));              // đầu
                //rects.Add(new BanisterRect(L, handrailBottom + m * L,
                //                           i.HandrailExtend, hd, true));              // cuối
                // Lan can nghiêng: MỘT hình liền khối gồm đoạn dốc và hai đầu nằm ngang dài Extend
                rects.Add(new BanisterRect(0, handrailBottom, L, hd, i.HandrailExtend));
            }

            // 2. Trụ lớn: tính vị trí trước, chia đều thành n nhịp
            int n = Math.Max(1, (int)Math.Ceiling(L / i.ColumnGap));
            double step = L / n;
            var columns = new List<(double x0, double x1)>();

            for (int c = 0; c <= n; c++)
            {
                bool isSide = (c == 0 || c == n);
                if (isSide && !i.HasSideColumns) continue;

                double x = c == 0 ? 0
                         : c == n ? L - i.ColumnDia
                         : c * step - i.ColumnDia / 2;
                columns.Add((x, x + i.ColumnDia));
                rects.Add(new BanisterRect(x, 0, i.ColumnDia, handrailBottom));
            }

            // 3. Thanh ngang trên và dưới: cắt bỏ phần nằm trong trụ
            AddRailSegments(rects, columns, L, railBottom, rd);
            AddRailSegments(rects, columns, L, lowRailY, rd);

            // 4. Song đứng trong từng nhịp
            double poleH = railBottom - poleBottom;
            if (poleH > 0)
            {
                for (int b = 0; b < n; b++)
                {
                    double x0 = b == 0
                        ? (i.HasSideColumns ? i.ColumnDia : 0)
                        : b * step + i.ColumnDia / 2;
                    double x1 = b == n - 1
                        ? (i.HasSideColumns ? L - i.ColumnDia : L)
                        : (b + 1) * step - i.ColumnDia / 2;

                    double clear = x1 - x0;
                    if (clear <= 0) continue;

                    int cnt = Math.Max(0, (int)Math.Ceiling((clear - i.PoleGap) / (i.PoleGap + i.PoleDia)));
                    double gap = (clear - cnt * i.PoleDia) / (cnt + 1);

                    for (int j = 0; j < cnt; j++)
                    {
                        double px = x0 + gap * (j + 1) + j * i.PoleDia;
                        rects.Add(new BanisterRect(px, poleBottom, i.PoleDia, poleH));
                    }
                }
            }
            return rects;
        }

        // Thêm một thanh ngang dài L, nhưng bỏ các đoạn trùng với trụ
        private static void AddRailSegments(List<BanisterRect> rects,
                                            List<(double x0, double x1)> columns,
                                            double L, double y, double h)
        {
            const double eps = 1e-6;
            double cursor = 0;

            foreach (var col in columns)               // trụ đã xếp tăng dần theo x
            {
                if (col.x0 > cursor + eps)
                    rects.Add(new BanisterRect(cursor, y, col.x0 - cursor, h));
                cursor = Math.Max(cursor, col.x1);
            }

            if (cursor < L - eps)
                rects.Add(new BanisterRect(cursor, y, L - cursor, h));
        }
    }
}