using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Tools.ViewModel
{
    public class AboutExpressToolVM : BaseViewModel
    {
        private string _currentVersion;
        public string CurrentVersion
        {
            get => _currentVersion;
            set
            {
                _currentVersion = value;
                OnPropertyChanged();
            }
        }
        public RelayCommand HyperlinkCmd { get; set; }

        public RelayCommand CloseCmd { get; set; }

    }
}
