using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Tools.Model;
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
        private bool? _dialogResult;
        private bool _acceptRequested, _pickRequested;
        private Dictionary<string, short> _layerColors = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        private readonly IAciColorPickerService _colorPicker;
        //private bool _sloped;
        //public bool Sloped { get => _sloped; set => Set(ref _sloped, value); }
        public ObservableCollection<string> LayerNames { get; } = new ObservableCollection<string>();
        

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
        public bool? DialogResult { get => _dialogResult; private set => Set(ref _dialogResult, value); }
        public bool AcceptRequested => _acceptRequested;
        public bool PickRequested => _pickRequested;

        public RelayCommand ResetCmd { get; }
        public RelayCommand AcceptCmd { get; }
        public RelayCommand PickCmd { get; }
        public RelayCommand ChooseColorCmd { get; }
        public RelayCommand CancelCmd { get; }

        public BanisterVM(IAciColorPickerService colorPicker)
        {
            _colorPicker = colorPicker;
            Load(new BanisterInput());
            ResetCmd = new RelayCommand(() => Load(new BanisterInput()));
            AcceptCmd = new RelayCommand(() =>
            {
                _acceptRequested = true;
                OnPropertyChanged(nameof(AcceptRequested));
                DialogResult = true;
            });
            PickCmd = new RelayCommand(() =>
            {
                _pickRequested = true;
                OnPropertyChanged(nameof(PickRequested));
                DialogResult = false;
            });
            CancelCmd = new RelayCommand(() => DialogResult = false);
            ChooseColorCmd = new RelayCommand(() =>
            {
                short current = short.TryParse(ColorIndex, out short aci) && aci >= 1 && aci <= 255 ? aci : (short)7;
                short? selected = _colorPicker?.PickColor(current);
                if (selected.HasValue) ColorIndex = selected.Value.ToString();
            });
        }

        public void ResetDialogRequest()
        {
            _acceptRequested = false;
            _pickRequested = false;
            OnPropertyChanged(nameof(AcceptRequested));
            OnPropertyChanged(nameof(PickRequested));
            DialogResult = null;
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

        public void ApplyPickedLayer(string layerName, short aci)
        {
            LayerName = layerName;
            ColorIndex = aci.ToString();
        }

        private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
