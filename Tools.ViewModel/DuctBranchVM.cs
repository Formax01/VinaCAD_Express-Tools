using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public class DuctBranchVM : INotifyPropertyChanged
    {
        private static readonly Dictionary<DuctBranchType, DuctBranchVM> _cache = new Dictionary<DuctBranchType, DuctBranchVM>();

        private double _mainWidth = DuctBranchSettings.DefaultMainWidth;
        private double _mainHeight = DuctBranchSettings.DefaultMainHeight;
        private double _throughWidth = DuctBranchSettings.DefaultThroughWidth;
        private double _throughHeight = DuctBranchSettings.DefaultThroughHeight;
        private double _branchWidth = DuctBranchSettings.DefaultBranchWidth;
        private double _branchHeight = DuctBranchSettings.DefaultBranchHeight;
        private double _radius = DuctBranchSettings.DefaultBranchWidth * DuctBranchSettings.DefaultRadiusRatio;
        private double _branch2Width = DuctBranchSettings.DefaultBranchWidth;
        private double _radius2 = DuctBranchSettings.DefaultBranchWidth * DuctBranchSettings.DefaultRadiusRatio;
        private RadiusMode _radiusMode = DuctBranchSettings.DefaultRadiusMode;
        private ElbowSide _side = DuctBranchSettings.DefaultSide;
        private string _layerName = DuctBranchSettings.DefaultLayer;

        public static DuctBranchVM Get(DuctBranchType type)
        {
            DuctBranchVM vm;
            if (!_cache.TryGetValue(type, out vm))
            {
                vm = new DuctBranchVM(type);
                _cache[type] = vm;
            }
            return vm;
        }

        public DuctBranchVM(DuctBranchType type)
        {
            Type = type;
        }

        public DuctBranchType Type { get; private set; }

        public double MainWidth 
        { 
            get => _mainWidth; 
            set => Set(ref _mainWidth, value); 
        }

        public double MainHeight 
        { 
            get => _mainHeight; 
            set => Set(ref _mainHeight, value); 
        }

        public double ThroughWidth 
        {
            get => _throughWidth; 
            set => Set(ref _throughWidth, value);
        }

        public double ThroughHeight 
        { 
            get => _throughHeight; 
            set => Set(ref _throughHeight, value); 
        }

        public double BranchWidth 
        { 
            get => _branchWidth; 
            set => Set(ref _branchWidth, value); 
        }
        public double BranchHeight 
        { 
            get => _branchHeight; 
            set => Set(ref _branchHeight, value); 
        }

        public double Radius 
        { 
            get => _radius; 
            set => Set(ref _radius, value); 
        }

        public double Branch2Width 
        { 
            get => _branch2Width; 
            set => Set(ref _branch2Width, value); 
        }

        public double Radius2 
        { 
            get => _radius2; 
            set => Set(ref _radius2, value); 
        }
        public RadiusMode RadiusMode 
        { 
            get => _radiusMode; 
            set => Set(ref _radiusMode, value); 
        }
        public ElbowSide Side 
        { 
            get => _side;
            set => Set(ref _side, value); 
        }
        public string LayerName 
        { 
            get => _layerName; 
            set => Set(ref _layerName, value); 
        }

        public bool NeedsThrough
        {
            get => Type != DuctBranchType.EEY;
        }

        public bool NeedsBranch2
        {
            get => Type == DuctBranchType.EEZ;
        }

        public bool NeedsSide
        {
            get => Type == DuctBranchType.EEX;
        }

        public double SuggestThroughWidth()
        {
            return ThroughWidth > 0 && ThroughWidth <= MainWidth ? ThroughWidth : MainWidth;
        }

        public double SuggestBranchWidth()
        {
            return Type == DuctBranchType.EEY ? MainWidth / 2.0 : BranchWidth;
        }

        public double SuggestRadius(double branchWidth)
        {
            return DuctBranchSettings.DefaultRadiusRatio * branchWidth;
        }

        public double ResolveCenterRadius(double width, double radius)
        {
            return RadiusMode == RadiusMode.Center
                ? radius
                : radius + width / 2.0;
        }

        public double Branch1CenterRadius
        {
            get => ResolveCenterRadius(BranchWidth, Radius);
        }

        public double EffectiveBranch2Width
        {
            get => NeedsBranch2 ? Branch2Width : BranchWidth;
        }

        public double Branch2CenterRadius
        {
            get => NeedsBranch2
                ? ResolveCenterRadius(Branch2Width, Radius2)
                : Branch1CenterRadius;
        }

        public DuctBranchModel BuildModel(Point3d insertPoint, Vector3d direction)
        {
            return new DuctBranchModel
            {
                Type = Type,
                InsertPoint = insertPoint,
                Direction = direction.GetNormal(),
                MainWidth = MainWidth,
                MainHeight = MainHeight,
                ThroughWidth = NeedsThrough ? ThroughWidth : 0,
                ThroughHeight = NeedsThrough ? ThroughHeight : 0,
                BranchHeight = BranchHeight,
                Branch1Width = BranchWidth,
                Branch1CenterRadius = Branch1CenterRadius,
                Branch2Width = EffectiveBranch2Width,
                Branch2CenterRadius = Branch2CenterRadius,
                BranchSide = Side,
                LayerName = LayerName
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