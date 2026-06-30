using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class DanhSoHieuTKTSVM : BaseViewModel
    {
        public DanhSoHieuTKTSVM()
        {
            IsCoDinh = true;
            IsLeftToRight = true;
            SoHieuStart = "X";
            HookLength = "100";
            Scale = "1/100";
            ChieuDayBTBaoVe = 25;
        }
        public RelayCommand DanhSoCmd { get; set; }
        public RelayCommand DrawTableCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }

        private bool _isCoDinh;
        public bool IsCoDinh
        {
            get
            {
                return _isCoDinh;
            }
            set
            {
                if (_isCoDinh != value)
                {
                    _isCoDinh = value;
                    OnPropertyChanged(nameof(IsCoDinh));
                }
            }
        }

        private bool _isThucTe;
        public bool IsThucTe
        {
            get
            {
                return _isThucTe;
            }
            set
            {
                if (_isThucTe != value)
                {
                    _isThucTe = value;
                    OnPropertyChanged(nameof(IsThucTe));
                }
            }
        }

        private string _hookLength;
        public string HookLength
        {
            get => _hookLength;
            set
            {
                _hookLength = value;
                OnPropertyChanged(nameof(HookLength));
            }
        }
        private double _chieuDayBTBaoVe;
        public double ChieuDayBTBaoVe
        {
            get => _chieuDayBTBaoVe;
            set
            {
                _chieuDayBTBaoVe = value;
                OnPropertyChanged(nameof(ChieuDayBTBaoVe));
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

        private string _soHieuStart;
        public string SoHieuStart
        {
            get => _soHieuStart;
            set
            {
                _soHieuStart = value;
                OnPropertyChanged(nameof(SoHieuStart));
            }
        }

        private string _scale;
        public string Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                OnPropertyChanged(nameof(Scale));
            }
        }
    }
}
