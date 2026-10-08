using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Colors;

namespace Tools.VinaCad.Helper.Helper
{
    public static class AciColorHelper
    {
        private static readonly byte[] Grays = { 51, 91, 132, 173, 214, 255 };       // ACI 250..255
        private static readonly double[] Values = { 1.0, 0.65, 0.5, 0.3, 0.15 };    // độ sáng theo cặp
        private static readonly int[] DarkOffsets = { 8, 6, 4, 2, 0 };
        private static readonly int[] LightOffsets = { 1, 3, 5, 7, 9 };

        public static bool IsValidIndex(short aci) => aci >= 1 && aci <= 255;

        public static bool TryParseIndex(string text, out short aci) =>
            short.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out aci) && IsValidIndex(aci);

        public static IReadOnlyList<short> GetDarkPalette() => GetHuePalette(DarkOffsets);

        public static IReadOnlyList<short> GetLightPalette() => GetHuePalette(LightOffsets);

        public static IReadOnlyList<short> GetStandardPalette()
        {
            var indices = new List<short>(15);
            for (short aci = 1; aci <= 9; aci++) indices.Add(aci);
            for (short aci = 250; aci <= 255; aci++) indices.Add(aci);
            return indices;
        }

        public static string Describe(short aci)
        {
            var (r, g, b) = ToRgb(aci);
            return $"ACI {aci}  (R {r}, G {g}, B {b})";
        }

        private static IReadOnlyList<short> GetHuePalette(int[] offsets)
        {
            var indices = new List<short>(120);
            foreach (int offset in offsets)
                for (int n = 0; n < 24; n++)
                    indices.Add((short)(10 + n * 10 + offset));
            return indices;
        }

        public static (byte R, byte G, byte B) ToRgb(short aci)
        {
            switch (aci)
            {
                case 1: return (255, 0, 0);
                case 2: return (255, 255, 0);
                case 3: return (0, 255, 0);
                case 4: return (0, 255, 255);
                case 5: return (0, 0, 255);
                case 6: return (255, 0, 255);
                case 7: return (255, 255, 255);
                case 8: return (128, 128, 128);
                case 9: return (192, 192, 192);
            }

            if (aci >= 250 && aci <= 255)
            {
                byte g = Grays[aci - 250];
                return (g, g, g);
            }

            if (aci >= 10 && aci <= 249)
            {
                int n = (aci - 10) / 10;          // 0..23: màu sắc (cách nhau 15 độ)
                int p = (aci - 10) % 10;          // 0..9: vị trí trong nhóm 10 màu
                double hue = n * 15.0;
                double sat = (p % 2 == 0) ? 1.0 : 0.5;
                double val = Values[p / 2];
                return HsvToRgb(hue, sat, val);
            }

            return (160, 160, 160);
        }

        private static (byte, byte, byte) HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r, g, b;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return ((byte)Math.Round((r + m) * 255),
                    (byte)Math.Round((g + m) * 255),
                    (byte)Math.Round((b + m) * 255));
        }
    }
}
