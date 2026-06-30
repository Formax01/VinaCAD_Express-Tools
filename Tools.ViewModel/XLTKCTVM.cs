using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class XLTKCTVM : BaseViewModel
    {

        public XLTKCTVM()
        {
            LapD10 = 36;
            LapD10D16 = 44;
            LapD16 = 44;
            DulThem = 0;
            IsDongDat = true;
            HookEarthquake = 10;
            HookNormal = 8;
        }

        public RelayCommand XacLapCmd { get; set; }
        public RelayCommand StandardTableCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }
        

        private double _lapD10;
        public double LapD10
        {
            get
            {
                return _lapD10;
            }
            set
            {
                if (_lapD10 != value)
                {
                    _lapD10 = value;
                    OnPropertyChanged(nameof(LapD10));
                }
            }
        }

        private double _lapD10D16;
        public double LapD10D16
        {
            get
            {
                return _lapD10D16;
            }
            set
            {
                if (_lapD10D16 != value)
                {
                    _lapD10D16 = value;
                    OnPropertyChanged(nameof(LapD10D16));
                }
            }
        }

        private double _lapD16;
        public double LapD16
        {
            get
            {
                return _lapD16;
            }
            set
            {
                if (_lapD16 != value)
                {
                    _lapD16 = value;
                    OnPropertyChanged(nameof(LapD16));
                }
            }
        }

        private double _dulThem;
        public double DulThem
        {
            get
            {
                return _dulThem;
            }
            set
            {
                if (_dulThem != value)
                {
                    _dulThem = value;
                    OnPropertyChanged(nameof(DulThem));
                }
            }
        }

        private double _hookEarthquake;
        public double HookEarthquake
        {
            get
            {
                return _hookEarthquake;
            }
            set
            {
                if (_hookEarthquake != value)
                {
                    _hookEarthquake = value;
                    OnPropertyChanged(nameof(HookEarthquake));
                }
            }
        }

        private double _hookNormal;
        public double HookNormal
        {
            get
            {
                return _hookNormal;
            }
            set
            {
                if (_hookNormal != value)
                {
                    _hookNormal = value;
                    OnPropertyChanged(nameof(HookNormal));
                }
            }
        }

        private bool _isDongDat;
        public bool IsDongDat
        {
            get
            {
                return _isDongDat;
            }
            set
            {
                if (_isDongDat != value)
                {
                    _isDongDat = value;
                    OnPropertyChanged(nameof(IsDongDat));
                }
            }
        }

        private bool _isNotDongDat;
        public bool IsNotDongDat
        {
            get
            {
                return _isNotDongDat;
            }
            set
            {
                if (_isNotDongDat != value)
                {
                    _isNotDongDat = value;
                    OnPropertyChanged(nameof(IsNotDongDat));
                }
            }
        }
    }
}
