using PrMVVMCore;
using System.Collections.ObjectModel;
using System.Linq;
using Tools.Model;

namespace Tools.ViewModel
{
    public class XLDatTenCocVM : BaseViewModel
    {
        public RelayCommand AutoDetectCmd { get; set; }
        public RelayCommand SaveCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }

        private int _startNumber = 1;
        public int StartNumber
        {
            get => _startNumber;
            set
            {
                if (_startNumber != value)
                {
                    _startNumber = value;
                    OnPropertyChanged(nameof(StartNumber));
                }
            }
        }

        private double _textHeight = 300.0;
        public double TextHeight
        {
            get => _textHeight;
            set
            {
                if (_textHeight != value)
                {
                    _textHeight = value;
                    OnPropertyChanged(nameof(TextHeight));
                }
            }
        }

        // 0..3 mapping for ordering
        private int _ordering = 0;
        public int Ordering
        {
            get => _ordering;
            set
            {
                if (_ordering != value)
                {
                    _ordering = value;
                    OnPropertyChanged(nameof(Ordering));
                }
            }
        }

        private string _prefix;
        public string Prefix
        {
            get => _prefix;
            set
            {
                var normalized = value?.ToUpperInvariant() ?? string.Empty;
                if (_prefix != normalized)
                {
                    _prefix = normalized;
                    OnPropertyChanged(nameof(Prefix));
                }
            }
        }

        // Text styles from drawing (not system fonts)
        public ObservableCollection<string> TextStyles { get; } = new ObservableCollection<string>();

        private string _selectedTextStyle = string.Empty;
        public string SelectedTextStyle
        {
            get => _selectedTextStyle;
            set
            {
                if (_selectedTextStyle != value)
                {
                    _selectedTextStyle = value;
                    OnPropertyChanged(nameof(SelectedTextStyle));
                }
            }
        }

        public XLDatTenCocVM()
        {
            // Initialize commands to non-null no-op implementations so consumers won't encounter null.
            // Callers can replace these with real implementations.
            AutoDetectCmd = new RelayCommand(() => { });
            SaveCmd = new RelayCommand(() => { });
            CancelCmd = new RelayCommand(() => { });
        }

        /// <summary>
        /// Clears and populates TextStyles from the drawing's TextStyleTable.
        /// </summary>
        public void LoadTextStylesFromDrawing(System.Collections.Generic.List<string> styleNames)
        {
            TextStyles.Clear();
            if (styleNames != null && styleNames.Count > 0)
            {
                foreach (var styleName in styleNames.OrderBy(s => s))
                {
                    TextStyles.Add(styleName);
                }
            }
        }
    }
}
