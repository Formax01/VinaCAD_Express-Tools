using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class BangTraNeoNoiCTVM : BaseViewModel
    {
        private string _imageNeoNoiVN;
        public string ImageNeoNoiVN
        {
            get => _imageNeoNoiVN;
            set
            {
                _imageNeoNoiVN = value;
                OnPropertyChanged(nameof(ImageNeoNoiVN));
            }
        }
        private string _imageNeoNoiChauAu;
        public string ImageNeoNoiChauAu
        {
            get => _imageNeoNoiChauAu;
            set
            {
                _imageNeoNoiChauAu = value;
                OnPropertyChanged(nameof(ImageNeoNoiChauAu));
            }
        }
    }
}
