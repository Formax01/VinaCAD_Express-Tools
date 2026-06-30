using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class BangTraBeTongVM : BaseViewModel
    {
        private string _imageCDBT;
        public string ImageCDBT
        {
            get => _imageCDBT;
            set
            {
                _imageCDBT = value;
                OnPropertyChanged(nameof(ImageCDBT));
            }
        }
    }
}
