using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class BangTraNeoCTVM : BaseViewModel
    {
        private string _imageNEO;
        public string ImageNEO
        {
            get => _imageNEO;
            set
            {
                _imageNEO = value;
                OnPropertyChanged(nameof(ImageNEO));
            }
        }
    }
}
