using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Modeling
{
    public static class XLDatTenCocSetting
    {
        // New: prefix for pile names, e.g. "P-" or empty string
        public static string Prefix { get; set; } = string.Empty;
        public static int StartNumber { get; set; } = 1;
        public static double TextHeight { get; set; } = 200.0;
        // 0 = L->R/Top->Bottom, 1 = R->L/Top->Bottom, 2 = Top->Bottom/ L->R, 3 = Bottom->Top/L->R
        public static int Ordering { get; set; } = 0;

        // Changed from FontName to TextStyleName (for VinaCad text styles)
        public static string TextStyleName { get; set; } = string.Empty;
    }
}