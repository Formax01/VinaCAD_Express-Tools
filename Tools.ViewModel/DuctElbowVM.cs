using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public class DuctElbowVM : INotifyPropertyChanged
    {
        private static readonly Dictionary<DuctElbowType, DuctElbowVM> _cache = new Dictionary<DuctElbowType, DuctElbowVM>();

        public static DuctElbowVM Get(DuctElbowType type)
        {
            DuctElbowVM vm;
            if (!_cache.TryGetValue(type, out vm))
            {
                vm = new DuctElbowVM(type);
                _cache[type] = vm;
            }
            return vm;
        }

        public DuctElbowVM(DuctElbowType type)
        {
            Type = type;
        }

        public DuctElbowType Type { get; private set; }

        private double _width1 = DuctElbowSettings.DefaultWidth1;
        public double Width1 { get { return _width1; } set { Set(ref _width1, value); } }

        private double _width2 = DuctElbowSettings.DefaultWidth2;
        public double Width2 { get { return _width2; } set { Set(ref _width2, value); } }

        private double _radius = DuctElbowSettings.DefaultRadius;
        public double Radius { get { return _radius; } set { Set(ref _radius, value); } }

        private RadiusMode _radiusMode = DuctElbowSettings.DefaultRadiusMode;
        public RadiusMode RadiusMode { get { return _radiusMode; } set { Set(ref _radiusMode, value); } }

        private double _angleDeg = DuctElbowSettings.DefaultAngleDeg;
        public double AngleDeg { get { return _angleDeg; } set { Set(ref _angleDeg, value); } }

        private ElbowSide _side = DuctElbowSettings.DefaultSide;
        public ElbowSide Side { get { return _side; } set { Set(ref _side, value); } }

        private double _dropLength = DuctElbowSettings.DefaultDropLength;
        public double DropLength { get { return _dropLength; } set { Set(ref _dropLength, value); } }

        private VerticalDirection _verticalDir = DuctElbowSettings.DefaultVerticalDir;
        public VerticalDirection VerticalDir { get { return _verticalDir; } set { Set(ref _verticalDir, value); } }

        private string _layerName = DuctElbowSettings.DefaultLayer;
        public string LayerName { get { return _layerName; } set { Set(ref _layerName, value); } }

        private bool _showCenterLine = DuctElbowSettings.DefaultShowCenterLine;
        public bool ShowCenterLine { get { return _showCenterLine; } set { Set(ref _showCenterLine, value); } }

        private bool _showLabel = DuctElbowSettings.DefaultShowLabel;
        public bool ShowLabel { get { return _showLabel; } set { Set(ref _showLabel, value); } }

        public double SuggestRadius(double width)
        {
            return DuctElbowSettings.DefaultRadiusRatio * width;
        }

        public double ResolveCenterRadius(double widthAtInlet)
        {
            return RadiusMode == RadiusMode.Center ? Radius : Radius + widthAtInlet / 2.0;
        }

        public DuctElbowModel BuildModel(Point3d insertPoint, Vector3d direction)
        {
            return new DuctElbowModel
            {
                Type = Type,
                InsertPoint = insertPoint,
                Direction = direction.GetNormal(),
                Width1 = Width1,
                Width2 = Type == DuctElbowType.Reducing ? Width2 : Width1,
                CenterRadius = ResolveCenterRadius(Width1),
                AngleDeg = AngleDeg,
                Side = Side,
                VerticalDir = VerticalDir,
                DropLength = DropLength,
                LayerName = LayerName,
                ShowCenterLine = ShowCenterLine,
                ShowLabel = ShowLabel
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
