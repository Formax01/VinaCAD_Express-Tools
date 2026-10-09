using PrMVVMCore;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Tools.Model;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Tools.VinaCad.Helper.Helper;

namespace Tools.ViewModel
{
    public sealed class AciPickerVM : BaseViewModel
    {
        private string _colorText;
        private short? _selectedAci;
        private bool? _dialogResult;
        private AciColorItem _selectedItem;
        private Brush _previewBrush = Brushes.Transparent;
        private string _description = string.Empty;

        public ObservableCollection<AciColorItem> DarkColors { get; } = new();
        public ObservableCollection<AciColorItem> LightColors { get; } = new();
        public ObservableCollection<AciColorItem> StandardColors { get; } = new();

        public string ColorText
        {
            get => _colorText;
            set
            {
                if (!Set(ref _colorText, value)) return;
                if (AciColorHelper.TryParseIndex(value, out short aci))
                    Select(aci, false);
                OnPropertyChanged(nameof(CanAccept));
            }
        }

        public short? SelectedAci => _selectedAci;
        public Brush PreviewBrush => _previewBrush;
        public string Description => _description;
        public bool CanAccept => AciColorHelper.TryParseIndex(ColorText, out _);
        public bool? DialogResult { get => _dialogResult; private set => Set(ref _dialogResult, value); }
        public ICommand SelectColorCommand { get; }
        public ICommand AcceptCommand { get; }
        public ICommand CancelCommand { get; }

        public AciPickerVM(short current)
        {
            SelectColorCommand = new DelegateCommand(parameter =>
            {
                if (parameter is AciColorItem item) ColorText = item.Index.ToString(CultureInfo.InvariantCulture);
            });
            AcceptCommand = new DelegateCommand(parameter =>
            {
                if (TryAccept(out short _)) DialogResult = true;
            });
            CancelCommand = new DelegateCommand(_ => DialogResult = false);

            foreach (short aci in AciColorHelper.GetDarkPalette()) DarkColors.Add(CreateItem(aci));
            foreach (short aci in AciColorHelper.GetLightPalette()) LightColors.Add(CreateItem(aci));
            foreach (short aci in AciColorHelper.GetStandardPalette()) StandardColors.Add(CreateItem(aci));

            Select(AciColorHelper.IsValidIndex(current) ? current : (short)7, true);
        }

        public bool TryAccept(out short aci)
        {
            if (AciColorHelper.TryParseIndex(ColorText, out aci))
            {
                _selectedAci = aci;
                OnPropertyChanged(nameof(SelectedAci));
                return true;
            }

            aci = 0;
            return false;
        }

        private static AciColorItem CreateItem(short aci)
        {
            var (r, g, b) = AciColorHelper.ToRgb(aci);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            return new AciColorItem(aci, brush, AciColorHelper.Describe(aci));
        }

        private void Select(short aci, bool updateText)
        {
            _selectedAci = aci;
            if (updateText)
            {
                _colorText = aci.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(ColorText));
            }

            if (_selectedItem != null) _selectedItem.IsSelected = false;
            _selectedItem = FindItem(aci);
            if (_selectedItem != null) _selectedItem.IsSelected = true;

            var (r, g, b) = AciColorHelper.ToRgb(aci);
            _previewBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
            _description = AciColorHelper.Describe(aci);
            OnPropertyChanged(nameof(SelectedAci));
            OnPropertyChanged(nameof(PreviewBrush));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(CanAccept));
        }

        private AciColorItem FindItem(short aci)
        {
            foreach (var item in DarkColors) if (item.Index == aci) return item;
            foreach (var item in LightColors) if (item.Index == aci) return item;
            foreach (var item in StandardColors) if (item.Index == aci) return item;
            return null;
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private sealed class DelegateCommand : ICommand
        {
            private readonly Action<object> _execute;
            public DelegateCommand(Action<object> execute) => _execute = execute;
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => _execute(parameter);
            public event EventHandler CanExecuteChanged { add { } remove { } }
        }
    }
}
