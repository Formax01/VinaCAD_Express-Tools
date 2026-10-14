using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public class DuctElbowVM : INotifyPropertyChanged
    {
        private static readonly Dictionary<DuctElbowType, DuctElbowVM> Cache = new Dictionary<DuctElbowType, DuctElbowVM>();

        private double _width1 = DuctElbowSettings.DefaultWidth1;
        private double _width2 = DuctElbowSettings.DefaultWidth2;
        private double _radius = DuctElbowSettings.DefaultRadius;
        private RadiusMode _radiusMode = DuctElbowSettings.DefaultRadiusMode;
        private double _angleDeg = DuctElbowSettings.DefaultAngleDeg;
        private ElbowSide _side = DuctElbowSettings.DefaultSide;
        private double _dropLength = DuctElbowSettings.DefaultDropLength;
        private VerticalDirection _verticalDir = DuctElbowSettings.DefaultVerticalDir;
        private string _layerName = DuctElbowSettings.DefaultLayer;
        private bool _showCenterLine = DuctElbowSettings.DefaultShowCenterLine;
        private bool _showLabel = DuctElbowSettings.DefaultShowLabel;

        public static DuctElbowVM Get(DuctElbowType type)
        {
            if (!Cache.TryGetValue(type, out DuctElbowVM vm))
            {
                vm = new DuctElbowVM(type);
                Cache[type] = vm;
            }
            return vm;
        }

        public DuctElbowVM(DuctElbowType type)
        {
            Type = type;
        }

        public DuctElbowType Type { get; set; }

        public double Width1
        {
            get => _width1;
            set => Set(ref _width1, value);
        }

        public double Width2
        {
            get => _width2;
            set => Set(ref _width2, value);
        }

        public double Radius
        {
            get => _radius;
            set => Set(ref _radius, value);
        }

        public RadiusMode RadiusMode
        {
            get => _radiusMode;
            set => Set(ref _radiusMode, value);
        }

        public double AngleDeg
        {
            get => _angleDeg;
            set => Set(ref _angleDeg, value);
        }

        public ElbowSide Side
        {
            get => _side;
            set => Set(ref _side, value);
        }

        public double DropLength
        {
            get => _dropLength;
            set => Set(ref _dropLength, value);
        }

        public VerticalDirection VerticalDir
        {
            get => _verticalDir;
            set => Set(ref _verticalDir, value);
        }

        public string LayerName
        {
            get => _layerName;
            set => Set(ref _layerName, value);
        }

        public bool ShowCenterLine
        {
            get => _showCenterLine;
            set => Set(ref _showCenterLine, value);
        }

        public bool ShowLabel
        {
            get => _showLabel;
            set => Set(ref _showLabel, value);
        }

        public double SuggestRadius(double width)
        {
            return DuctElbowSettings.DefaultRadiusRatio * width;
        }

        public double ResolveCenterRadius(double widthAtInlet)
        {
            return RadiusMode == RadiusMode.Center
                ? Radius
                : Radius + widthAtInlet / 2.0;
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

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
