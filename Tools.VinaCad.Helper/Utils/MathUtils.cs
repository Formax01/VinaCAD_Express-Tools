using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Helper.Utils
{
    public class MathUtils
    {
        public static double LamTronBoiCua5(double value)
        {
            return Math.Round(value / 5.0) * 5.0;
        }
        public static double LamTronXuongBoiCua5(double value)
        {
            return Math.Floor(value / 5.0) * 5.0;
        }
        public static string FormatNumber(double value)
        {
            return Math.Round(value, 2).ToString();
        }
        public static double ToDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            return double.TryParse(value, out double result)
                ? result
                : 0;
        }
    }
}
