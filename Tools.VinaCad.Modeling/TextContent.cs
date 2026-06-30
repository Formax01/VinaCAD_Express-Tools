using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace Tools.VinaCad.Modeling
{
    public static class TextContent
    {
        public static string Content { get; set; }
        public static bool IsExactMatch { get; set; }
        public static bool IsContainsMatch { get; set; }
        public static Point3d Position { get; set; }
    }
}
