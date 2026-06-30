using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class BangTraDTCTWM : BaseViewModel
    {
        private string _imageDTCT;
        public string ImageDTCT
        {
            get => _imageDTCT;
            set
            {
                _imageDTCT = value;
                OnPropertyChanged(nameof(ImageDTCT));
            }
        }
    }
}
