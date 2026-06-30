using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Modeling
{
    public static class NumberingDrawingsSetting
    {
        public static string BlockName { get; set; }

        public static object DrawingNoTag { get; set; }

        public static object DrawingTitle1Tag { get; set; }

        public static object DrawingTitle2Tag { get; set; }

        public static object DrawingTitle3Tag { get; set; }
        public static int  Scale { get; set; }

        public static double Tolerance { get; set; }

        public static bool IsLeftToRight { get; set; }

        public static bool IsTopToBottom { get; set; }

        public static string Prefix { get; set; }

        public static string Suffix { get; set; }
    }
}
