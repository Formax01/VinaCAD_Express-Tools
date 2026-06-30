
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class XLSDMVM : BaseViewModel
    {
        public XLSDMVM()
        {
            Tolerance = 10;
            IsTopToBottom = true;
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
