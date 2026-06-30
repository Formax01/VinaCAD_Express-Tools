using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class ToaDoCocVM : BaseViewModel

    {
        public ToaDoCocVM()
        {
            CheckDistance = 0.05;
            Scale = "1/100";
        }

        private double _checkDistance;
        public double CheckDistance
        {
            get
            {
                return _checkDistance;
            }
            set
            {
                if (_checkDistance != value)
                {
                    _checkDistance = value;
                    OnPropertyChanged(nameof(CheckDistance));
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

        public RelayCommand CancelCmd { get; set; }
        public RelayCommand SelectPileCmd { get; set; }
    }
}
