using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Modeling
{
    public class XLDCDSetting
    {
        public static double LechPhuong { get; set; }

        public static double LechChanDim { get; set; }

        public static bool IsLeftToRightHorizontal { get; set; }

        public static bool IsRightToLeftHorizontal { get; set; }

        public static bool IsTopToBottomVertical { get; set; }

        public static bool IsBottomToTopVertical { get; set; }

        public static bool IsBottomToTopAligned { get; set; }

        public static bool IsTopToBottomAligned { get; set; }
    }
}
