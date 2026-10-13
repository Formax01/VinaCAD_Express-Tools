using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using PrMVVMCore;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;

namespace Tools.ViewModel
{
    public sealed class AciPickerVM : BaseViewModel
    {
        private string _colorText;
        private bool? _dialogResult;
        private bool _canAccept;
        private Brush _previewBrush = Brushes.Transparent;
        private string _description = string.Empty;

        public ObservableCollection<AciColorItem> DarkColors { get; } = new();
        public ObservableCollection<AciColorItem> LightColors { get; } = new();
        public ObservableCollection<AciColorItem> StandardColors { get; } = new();

        public string ColorText { get => _colorText; set => OnPropertyChanged(ref _colorText, value); }
        public bool CanAccept { get => _canAccept; set => OnPropertyChanged(ref _canAccept, value); }
        public Brush PreviewBrush { get => _previewBrush; set => OnPropertyChanged(ref _previewBrush, value); }
        public string Description { get => _description; set => OnPropertyChanged(ref _description, value); }
        public bool? DialogResult { get => _dialogResult; private set => OnPropertyChanged(ref _dialogResult, value); }

        public ICommand SelectColorCommand { get; }
        public ICommand AcceptCommand { get; }
        public ICommand CancelCommand { get; }

        public AciPickerVM()
        {
            SelectColorCommand = new RelayCommand<object>(p =>
            {
                if (p is AciColorItem item) ColorText = item.Index.ToString(CultureInfo.InvariantCulture);
            });
            AcceptCommand = new RelayCommand(_ => CanAccept, () => DialogResult = true);
            CancelCommand = new RelayCommand(() => DialogResult = false);
        }
    }
}