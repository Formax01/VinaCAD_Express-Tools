using System.Collections.Generic;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public class DuctElbowVM
    {
        private static readonly Dictionary<DuctElbowType, DuctElbowVM> Cache = new Dictionary<DuctElbowType, DuctElbowVM>();

        public static DuctElbowVM Get(DuctElbowType type)
        {
            DuctElbowVM vm;
            if (!Cache.TryGetValue(type, out vm))
            {
                vm = new DuctElbowVM(type);
                Cache[type] = vm;
            }
            return vm;
        }

        public DuctElbowVM(DuctElbowType type)
        {
            Type = type;
            ResetDefaults();
        }

        public DuctElbowType Type { get; set; }

        public double Width1 { get; set; }              // bề rộng đầu vào
        public double Width2 { get; set; }              // bề rộng đầu ra (chỉ ống giảm cấp dùng)
        public double Radius { get; set; }              // bán kính cong
        public RadiusMode RadiusMode { get; set; }      // Radius tính theo đường tim hay theo thành trong
        public double AngleDeg { get; set; }            // góc cút (độ)
        public ElbowSide Side { get; set; }             // rẽ trái / phải
        public double DropLength { get; set; }          // chiều dài đoạn hạ/ngóc
        public VerticalDirection VerticalDir { get; set; }  // hạ hay ngóc
        public string LayerName { get; set; }
        public bool ShowCenterLine { get; set; }
        public bool ShowLabel { get; set; }

        public void ResetDefaults()
        {
            Width1 = DuctElbowSettings.DefaultWidth1;
            Width2 = DuctElbowSettings.DefaultWidth2;
            Radius = DuctElbowSettings.DefaultRadius;
            RadiusMode = DuctElbowSettings.DefaultRadiusMode;
            AngleDeg = DuctElbowSettings.DefaultAngleDeg;
            Side = DuctElbowSettings.DefaultSide;
            DropLength = DuctElbowSettings.DefaultDropLength;
            VerticalDir = DuctElbowSettings.DefaultVerticalDir;
            LayerName = DuctElbowSettings.DefaultLayer;
            ShowCenterLine = DuctElbowSettings.DefaultShowCenterLine;
            ShowLabel = DuctElbowSettings.DefaultShowLabel;
        }

        public double SuggestRadius(double width)
        {
            return DuctElbowSettings.DefaultRadiusRatio * width;
        }

        public double ResolveCenterRadius(double widthAtInlet)
        {
            if (RadiusMode == RadiusMode.Center) return Radius;
            return Radius + widthAtInlet / 2.0;
        }

        public DuctElbowModel BuildModel(Point3d insertPoint, Vector3d direction)
        {
            double width2 = Type == DuctElbowType.Reducing ? Width2 : Width1;

            return new DuctElbowModel
            {
                Type = Type,
                InsertPoint = insertPoint,
                Direction = direction.GetNormal(),
                Width1 = Width1,
                Width2 = width2,
                CenterRadius = ResolveCenterRadius(Width1),
                AngleDeg = AngleDeg,
                Side = Side,
                VerticalDir = VerticalDir,
                DropLength = DropLength,
                Height = Height,
                LayerName = LayerName,
                ShowCenterLine = ShowCenterLine,
                ShowLabel = ShowLabel
            };
        }
    }
}