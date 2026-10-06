using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Tools.Model;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
namespace Tools.ViewModel
{
    public class BanisterVM : BaseViewModel
    {
        // ---- Helper set property ----
        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);      // <-- xem lưu ý bên dưới
        }

        private string _layerName, _colorIndex, _totalHeight, _handrailDia, _handrailExtend,
                       _railDia, _topGap, _bottomGap, _columnDia, _columnGap, _poleDia, _poleGap;
        private bool _hasSideColumns, _toGroup;
        //private bool _sloped;
        //public bool Sloped { get => _sloped; set => Set(ref _sloped, value); }
        public ObservableCollection<string> LayerNames { get; } = new ObservableCollection<string>();
        //public string[] Units { get; } = { "Auto", "mm", "cm", "m" };

        //private string _selectedUnit = "Auto";
        //public string SelectedUnit { get => _selectedUnit; set => Set(ref _selectedUnit, value); }

        private Dictionary<string, short> _layerColors =
            new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        public string LayerName {
            get => _layerName;
            set { Set(ref _layerName, value); SyncColorFromLayer(); }
        }
        public string ColorIndex {
            get => _colorIndex;
            set { Set(ref _colorIndex, value); OnPropertyChanged(nameof(ColorBrush)); }
        }
        public string TotalHeight { get => _totalHeight; set => Set(ref _totalHeight, value); }
        public string HandrailDia { get => _handrailDia; set => Set(ref _handrailDia, value); }
        public string HandrailExtend { get => _handrailExtend; set => Set(ref _handrailExtend, value); }
        public string RailDia { get => _railDia; set => Set(ref _railDia, value); }
        public string TopGap { get => _topGap; set => Set(ref _topGap, value); }
        public string BottomGap { get => _bottomGap; set => Set(ref _bottomGap, value); }
        public string ColumnDia { get => _columnDia; set => Set(ref _columnDia, value); }
        public string ColumnGap { get => _columnGap; set => Set(ref _columnGap, value); }
        public string PoleDia { get => _poleDia; set => Set(ref _poleDia, value); }
        public string PoleGap { get => _poleGap; set => Set(ref _poleGap, value); }
        public bool HasSideColumns { get => _hasSideColumns; set => Set(ref _hasSideColumns, value); }
        public bool ToGroup { get => _toGroup; set => Set(ref _toGroup, value); }

        public RelayCommand ResetCmd { get; }

        public BanisterVM()
        {
            Load(new BanisterInput());
            ResetCmd = new RelayCommand(() => Load(new BanisterInput()));
        }
        public void SetLayers(Dictionary<string, short> layers)
        {
            _layerColors = layers;
            LayerNames.Clear();
            foreach (var n in layers.Keys.OrderBy(x => x)) LayerNames.Add(n);
            SyncColorFromLayer();
        }

        private void SyncColorFromLayer()
        {
            if (!string.IsNullOrWhiteSpace(LayerName) && _layerColors.TryGetValue(LayerName.Trim(), out short aci))
                ColorIndex = aci.ToString();
        }

        public Brush ColorBrush
        {
            get
            {
                if (short.TryParse(ColorIndex, out short a) && a >= 1 && a <= 255)
                {
                    var c = AciColorHelper.ToRgb(a);
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
                }
                return Brushes.Transparent;
            }
        }
        // Đưa giá trị từ DTO lên form
        public void Load(BanisterInput i)
        {
            //Sloped = i.Sloped;
            LayerName = i.LayerName;
            ColorIndex = i.ColorIndex.ToString();
            TotalHeight = Fmt(i.TotalHeight);
            HandrailDia = Fmt(i.HandrailDia);
            HandrailExtend = Fmt(i.HandrailExtend);
            RailDia = Fmt(i.RailDia);
            TopGap = Fmt(i.TopGap);
            BottomGap = Fmt(i.BottomGap);
            ColumnDia = Fmt(i.ColumnDia);
            ColumnGap = Fmt(i.ColumnGap);
            PoleDia = Fmt(i.PoleDia);
            PoleGap = Fmt(i.PoleGap);
            HasSideColumns = i.HasSideColumns;
            ToGroup = i.ToGroup;
            //SelectedUnit = i.DrawingUnit;
        }

        // Parse + kiểm tra. Trả về false kèm thông báo lỗi.
        public void ApplyPickedLayer(string layerName, short aci)
        {
            LayerName = layerName;
            ColorIndex = aci.ToString();
        }
        public bool TryBuildInput(out BanisterInput input, out string error)
        {
            input = null; error = null;

            string layer = (LayerName ?? "").Trim();
            if (layer.Length == 0 || layer.IndexOfAny("<>/\\\":;?*|=`".ToCharArray()) >= 0)
            { error = "Tên layer không hợp lệ."; return false; }

            if (!short.TryParse(ColorIndex, out short aci) || aci < 1 || aci > 255)
            { error = "Màu layer (ACI) phải từ 1 đến 255."; return false; }

            if (!P(TotalHeight, "Total Height", true, out double h, ref error)) return false;
            if (!P(HandrailDia, "Handrail Dia", true, out double hd, ref error)) return false;
            if (!P(HandrailExtend, "Handrail Extend", false, out double he, ref error)) return false;
            if (!P(RailDia, "Rail Dia", true, out double rd, ref error)) return false;
            if (!P(TopGap, "Top Gap", false, out double tg, ref error)) return false;
            if (!P(BottomGap, "Bottom Gap", false, out double bg, ref error)) return false;
            if (!P(ColumnDia, "Column Dia", true, out double cd, ref error)) return false;
            if (!P(ColumnGap, "Column Gap", true, out double cg, ref error)) return false;
            if (!P(PoleDia, "Pole Dia", true, out double pd, ref error)) return false;
            if (!P(PoleGap, "Pole Gap", true, out double pg, ref error)) return false;

            if (hd + tg + rd + bg >= h)
            { error = "Total Height quá nhỏ so với Handrail Dia + Top Gap + Rail Dia + Bottom Gap."; return false; }
            if (cd >= cg)
            { error = "Column Gap phải lớn hơn Column Dia."; return false; }

            input = new BanisterInput
            {
                //Sloped = Sloped,
                LayerName = layer,
                ColorIndex = aci,
                TotalHeight = h,
                HandrailDia = hd,
                HandrailExtend = he,
                RailDia = rd,
                TopGap = tg,
                BottomGap = bg,
                ColumnDia = cd,
                ColumnGap = cg,
                HasSideColumns = HasSideColumns,
                PoleDia = pd,
                PoleGap = pg,
                //DrawingUnit = SelectedUnit,
                ToGroup = ToGroup

            };
            return true;
        }

        // ---- helpers ----
        private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool P(string text, string label, bool mustBePositive, out double value, ref string error)
        {
            string t = (text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            { error = $"{label}: giá trị không phải là số."; return false; }
            if (mustBePositive ? value <= 0 : value < 0)
            { error = mustBePositive ? $"{label} phải lớn hơn 0." : $"{label} không được âm."; return false; }
            return true;
        }
    }
}