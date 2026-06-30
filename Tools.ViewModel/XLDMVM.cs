using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class XLDMVM : BaseViewModel
    {

        public  XLDMVM ()
        {
            Tolerance = 100;
            TienTo = "KC-";
            HauTo = "01";
            Scale = "1/100";
        }
        public RelayCommand OkCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand ChoseBlockCmd { get; set; }

        private string _blockName;
        public string BlockName
        {
            get
            {
                return _blockName;
            }
            set
            {
                if (_blockName != value)
                {
                    _blockName = value;
                    OnPropertyChanged(nameof(BlockName));
                }
            }
        }

        private string _tienTo;
        public string TienTo
        {
            get
            {
                return _tienTo;
            }
            set
            {
                if (_tienTo != value)
                {
                    _tienTo = value;
                    OnPropertyChanged(nameof(TienTo));
                }
            }
        }

        private string _hauTo;
        public string HauTo
        {
            get
            {
                return _hauTo;
            }
            set
            {
                if (_hauTo != value)
                {
                    _hauTo = value;
                    OnPropertyChanged(nameof(HauTo));
                }
            }
        }

        private double _tolerance;
        public double Tolerance
        {
            get
            {
                return _tolerance;
            }
            set
            {
                if (_tolerance != value)
                {
                    _tolerance = value;
                    OnPropertyChanged(nameof(Tolerance));
                }
            }
        }

        private bool _isLeftToRight;
        public bool IsLeftToRight
        {
            get
            {
                return _isLeftToRight;
            }
            set
            {
                if (_isLeftToRight != value)
                {
                    _isLeftToRight = value;
                    OnPropertyChanged(nameof(IsLeftToRight));
                }
            }
        }

        private bool _isTopToBottom;
        public bool IsTopToBottom
        {
            get
            {
                return _isTopToBottom;
            }
            set
            {
                if (_isTopToBottom != value)
                {
                    _isTopToBottom = value;
                    OnPropertyChanged(nameof(IsTopToBottom));
                }
            }
        }

        private bool _isNumber;
        public bool IsNumber
        {
            get
            {
                return _isNumber;
            }
            set
            {
                if (_isNumber != value)
                {
                    _isNumber = value;
                    OnPropertyChanged(nameof(IsNumber));
                }
            }
        }

        private bool _isAlphaB;
        public bool IsAlphaB
        {
            get
            {
                return _isAlphaB;
            }
            set
            {
                if (_isAlphaB != value)
                {
                    _isAlphaB = value;
                    OnPropertyChanged(nameof(IsAlphaB));
                }
            }
        }

        private string _scale;
        public string Scale
        {
            get
            {
                return _scale;
            }
            set
            {
                if (_scale != value)
                {
                    _scale = value;
                    OnPropertyChanged(nameof(Scale));
                }
            }
        }
        private ObservableCollection<object> _kiHieuBanVeItems;
        public ObservableCollection<object> KiHieuBanVeItems
        {
            get { return _kiHieuBanVeItems; }
            set { _kiHieuBanVeItems = value; OnPropertyChanged(); }
        }

        private object _kiHieuBanVeSelected;
        public object KiHieuBanVeSelected
        {
            get { return _kiHieuBanVeSelected; }
            set
            {
                _kiHieuBanVeSelected = value;
                OnPropertyChanged();
            }
        }


        private ObservableCollection<object> _tenBanVe1Items;
        public ObservableCollection<object> TenBanVe1Items
        {
            get { return _tenBanVe1Items; }
            set { _tenBanVe1Items = value; OnPropertyChanged(); }
        }

        private object _tenBanVe1Selected;
        public object TenBanVe1Selected
        {
            get { return _tenBanVe1Selected; }
            set
            {
                _tenBanVe1Selected = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<object> _tenBanVe2Items;
        public ObservableCollection<object> TenBanVe2Items
        {
            get { return _tenBanVe2Items; }
            set { _tenBanVe2Items = value; OnPropertyChanged(); }
        }

        private object _tenBanVe2Selected;
        public object TenBanVe2Selected
        {
            get { return _tenBanVe2Selected; }
            set
            {
                _tenBanVe2Selected = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<object> _tenBanVe3Items;
        public ObservableCollection<object> TenBanVe3Items
        {
            get { return _tenBanVe3Items; }
            set { _tenBanVe3Items = value; OnPropertyChanged(); }
        }

        private object _tenBanVe3Selected;
        public object TenBanVe3Selected
        {
            get { return _tenBanVe3Selected; }
            set
            {
                _tenBanVe3Selected = value;
                OnPropertyChanged();
            }
        }

    }
}
