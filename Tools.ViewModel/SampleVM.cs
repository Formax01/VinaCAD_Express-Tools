using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class SampleVM : BaseViewModel
    {
        public RelayCommand ShowCmd { get; set; }

        private string _textSample;
        public string TextSample
        {
            get
            {
                return _textSample;
            }
            set
            {
                if (_textSample != value)
                {
                    _textSample = value;
                    OnPropertyChanged(nameof(TextSample));
                }
            }
        }
    }
}
