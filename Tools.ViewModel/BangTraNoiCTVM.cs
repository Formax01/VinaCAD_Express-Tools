using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class BangTraNoiCTVM : BaseViewModel
    {
        private string _imageNOI;
        public string ImageNOI
        {
            get => _imageNOI;
            set
            {
                _imageNOI = value;
                OnPropertyChanged(nameof(ImageNOI));
            }
        }
    }
}
