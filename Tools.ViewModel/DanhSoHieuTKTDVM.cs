using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class DanhSoHieuTKTDVM : BaseViewModel
    {
        public DanhSoHieuTKTDVM()
        {
            IsLeftToRight = true;
            SoHieuThepRenStart = "r";
            ChieuDaiCongThemTienRen = "50";
            SoHieuThepKhacStart = "x";
            Scale = "1/100";
        }

        public RelayCommand DanhSoCmd { get; set; }
        public RelayCommand DrawTableCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }

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

        private string _chieuDaiCongThemTienRen;
        public string ChieuDaiCongThemTienRen
        {
            get => _chieuDaiCongThemTienRen;
            set
            {
                _chieuDaiCongThemTienRen = value;
                OnPropertyChanged(nameof(ChieuDaiCongThemTienRen));
            }
        }

        private string _soHieuThepRenStart;
        public string SoHieuThepRenStart
        {
            get => _soHieuThepRenStart;
            set
            {
                _soHieuThepRenStart = value;
                OnPropertyChanged(nameof(SoHieuThepRenStart));
            }
        }

        private string _soHieuThepKhacStart;
        public string SoHieuThepKhacStart
        {
            get => _soHieuThepKhacStart;
            set
            {
                _soHieuThepKhacStart = value;
                OnPropertyChanged(nameof(SoHieuThepKhacStart));
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
