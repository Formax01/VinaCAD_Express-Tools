using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public static class DuctElbowSettings
    {
        public const double DefaultRadiusRatio = 1.0;    
        public const RadiusMode DefaultRadiusMode = RadiusMode.Inner;

        public const double MinAngle = 1.0;
        public const double MaxAngle = 180.0;

        public const double DefaultWidth1 = 300;
        public const double DefaultWidth2 = 200;
        public const double DefaultRadius = 300;
        public const double DefaultAngleDeg = 90;
        public const double DefaultDropLength = 500;
        public const ElbowSide DefaultSide = ElbowSide.Left;
        public const VerticalDirection DefaultVerticalDir = VerticalDirection.Down;

        public const string DefaultLayer = "DUCT_ELBOW";
        public const string GroupPrefix = "DUCTELBOW_";
        public const bool DefaultShowCenterLine = false;
        public const bool DefaultShowLabel = true;
    }
}
