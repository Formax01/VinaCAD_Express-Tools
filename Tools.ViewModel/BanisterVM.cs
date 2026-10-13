using PrMVVMCore;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;

namespace Tools.ViewModel
{
    public class BanisterVM : BaseViewModel
    {
        private string _layerName, _colorIndex, _totalHeight, _handrailDia, _handrailExtend,
                       _railDia, _topGap, _bottomGap, _columnDia, _columnGap, _poleDia, _poleGap;
        private bool _hasSideColumns, _toGroup;
        private bool? _dialogResult;
        private bool _acceptRequested, _pickRequested;
        private Brush _colorBrush = Brushes.Transparent;
        private readonly IAciColorPickerService _colorPicker;

        public ObservableCollection<string> LayerNames { get; } = new ObservableCollection<string>();

        public string LayerName { get => _layerName; set => OnPropertyChanged(ref _layerName, value); }
        public string ColorIndex { get => _colorIndex; set => OnPropertyChanged(ref _colorIndex, value); }
        public Brush ColorBrush { get => _colorBrush; set => OnPropertyChanged(ref _colorBrush, value); }
        public string TotalHeight { get => _totalHeight; set => OnPropertyChanged(ref _totalHeight, value); }
        public string HandrailDia { get => _handrailDia; set => OnPropertyChanged(ref _handrailDia, value); }
        public string HandrailExtend { get => _handrailExtend; set => OnPropertyChanged(ref _handrailExtend, value); }
        public string RailDia { get => _railDia; set => OnPropertyChanged(ref _railDia, value); }
        public string TopGap { get => _topGap; set => OnPropertyChanged(ref _topGap, value); }
        public string BottomGap { get => _bottomGap; set => OnPropertyChanged(ref _bottomGap, value); }
        public string ColumnDia { get => _columnDia; set => OnPropertyChanged(ref _columnDia, value); }
        public string ColumnGap { get => _columnGap; set => OnPropertyChanged(ref _columnGap, value); }
        public string PoleDia { get => _poleDia; set => OnPropertyChanged(ref _poleDia, value); }
        public string PoleGap { get => _poleGap; set => OnPropertyChanged(ref _poleGap, value); }
        public bool HasSideColumns { get => _hasSideColumns; set => OnPropertyChanged(ref _hasSideColumns, value); }
        public bool ToGroup { get => _toGroup; set => OnPropertyChanged(ref _toGroup, value); }
        public bool? DialogResult { get => _dialogResult; private set => OnPropertyChanged(ref _dialogResult, value); }
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
                
                short current = AciColorHelper.TryParseIndex(ColorIndex, out short aci) ? aci : (short)0;
                short? selected = _colorPicker?.PickColor(current);
                if (selected.HasValue) ColorIndex = selected.Value.ToString(CultureInfo.InvariantCulture);
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

        public void SetLayers(IEnumerable<string> names)
        {
            LayerNames.Clear();
            foreach (var n in names) LayerNames.Add(n);
        }

        // Đưa giá trị từ DTO lên form
        public void Load(BanisterInput i)
        {
            LayerName = i.LayerName;
            ColorIndex = i.ColorIndex.ToString(CultureInfo.InvariantCulture);
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
        }

        public void ApplyPickedLayer(string layerName, short aci)
        {
            LayerName = layerName;
            ColorIndex = aci.ToString(CultureInfo.InvariantCulture);
        }

        private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}