using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Modeling
{
    public static class XLSDMSetting
    {
        public static string BlockName { get; set; }
        public static object DrawingTitle1Tag { get; set; }

        public static object DrawingTitle2Tag { get; set; }

        public static object DrawingTitle3Tag { get; set; }

        public static double Tolerance { get; set; }

        public static bool IsLeftToRight { get; set; }

        public static bool IsTopToBottom { get; set; }

     
    }
}
