using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Model;

namespace Tools.ViewModel
{
    public class SettingKiemTraDimCongDonVM : BaseViewModel
    {
        public SettingKiemTraDimCongDonVM()
        {
            LechPhuong = 10;
            LechChanDim = 0.9;
            IsLeftToRightHorizontal = true;
            IsBottomToTopVertical = true;
            IsBottomToTopAligned = true;
        }

        public RelayCommand OKCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }

        private double _lechPhuong;
        public double LechPhuong
        {
            get => _lechPhuong;
            set
            {
                _lechPhuong = value;
                OnPropertyChanged(nameof(LechPhuong));
            }
        }
        private double _lechChanDim;
        public double LechChanDim
        {
            get => _lechChanDim;
            set
            {
                _lechChanDim = value;
                OnPropertyChanged(nameof(LechChanDim));
            }
        }

        private bool _isLeftToRightHorizontal;
        public bool IsLeftToRightHorizontal
        {
            get
            {
                return _isLeftToRightHorizontal;
            }
            set
            {
                if (_isLeftToRightHorizontal != value)
                {
                    _isLeftToRightHorizontal = value;
                    OnPropertyChanged(nameof(IsLeftToRightHorizontal));
                }
            }
        }

        private bool _isRightToLeftHorizontal;
        public bool IsRightToLeftHorizontal
        {
            get
            {
                return _isRightToLeftHorizontal;
            }
            set
            {
                if (_isRightToLeftHorizontal != value)
                {
                    _isRightToLeftHorizontal = value;
                    OnPropertyChanged(nameof(IsRightToLeftHorizontal));
                }
            }
        }

        private bool _isTopToBottomVertical;
        public bool IsTopToBottomVertical
        {
            get
            {
                return _isTopToBottomVertical;
            }
            set
            {
                if (_isTopToBottomVertical != value)
                {
                    _isTopToBottomVertical = value;
                    OnPropertyChanged(nameof(IsTopToBottomVertical));
                }
            }
        }

        private bool _isBottomToTopVertical;
        public bool IsBottomToTopVertical
        {
            get
            {
                return _isBottomToTopVertical;
            }
            set
            {
                if (_isBottomToTopVertical != value)
                {
                    _isBottomToTopVertical = value;
                    OnPropertyChanged(nameof(IsBottomToTopVertical));
                }
            }
        }

        private bool _isBottomToTopAligned;
        public bool IsBottomToTopAligned
        {
            get
            {
                return _isBottomToTopAligned;
            }
            set
            {
                if (_isBottomToTopAligned != value)
                {
                    _isBottomToTopAligned = value;
                    OnPropertyChanged(nameof(IsBottomToTopAligned));
                }
            }
        }

        private bool _isTopToBottomAligned;
        public bool IsTopToBottomAligned
        {
            get
            {
                return _isTopToBottomAligned;
            }
            set
            {
                if (_isTopToBottomAligned != value)
                {
                    _isTopToBottomAligned = value;
                    OnPropertyChanged(nameof(IsTopToBottomAligned));
                }
            }
        }
    }
}
