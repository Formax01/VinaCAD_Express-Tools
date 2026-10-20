using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public class DuctBranchVM
    {
        public readonly DuctBranchType Type;

        public double MainWidth, MainHeight;
        public double ThroughWidth, ThroughHeight;
        public double BranchWidth, Radius;
        public double Branch2Width, Radius2;
        public double BranchHeight;
        public ElbowSide Side;

        public DuctBranchVM(DuctBranchType type)
        {
            Type = type;
            ApplyPreset();
        }

        public bool HasThrough                                 // eex, eez
        { 
            get { return Type != DuctBranchType.EEY; } 
        }
        public bool HasBranch2                                  // eez
        { 
            get { return Type == DuctBranchType.EEZ; } 
        }   
        public bool HasSide                                      // eex
        { 
            get { return Type == DuctBranchType.EEX; } 
        }

        public void ApplyPreset()
        {
            DuctBranchPreset setting = DuctBranchSettings.PresetFor(Type);
            MainWidth = setting.MainWidth;
            MainHeight = setting.MainHeight;
            ThroughWidth = setting.ThroughWidth;
            ThroughHeight = setting.ThroughHeight;
            BranchWidth = setting.BranchWidth;
            Radius = setting.Radius;
            Branch2Width = setting.Branch2Width;
            Radius2 = setting.Radius2;
            BranchHeight = setting.BranchHeight;
            Side = setting.Side;
        }

        public DuctBranchModel ToModel(Point3d insertPoint, Vector3d direction)
        {
            return new DuctBranchModel
            {
                Type = Type,
                InsertPoint = insertPoint,
                Direction = direction.GetNormal(),
                LayerName = DuctBranchSettings.DefaultLayer,
                MainWidth = MainWidth,
                MainHeight = MainHeight,
                ThroughWidth = ThroughWidth,
                ThroughHeight = ThroughHeight,
                BranchHeight = BranchHeight,
                Branch1Width = BranchWidth,
                Branch1Radius = Radius,
                // eex, eey chỉ có một kiểu nhánh nên nhánh 2 giống nhánh 1
                Branch2Width = HasBranch2 ? Branch2Width : BranchWidth,
                Branch2Radius = HasBranch2 ? Radius2 : Radius,
                BranchSide = Side
            };
        }
    }
}