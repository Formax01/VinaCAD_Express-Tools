using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Model;

namespace Tools.ViewModel
{
    public class SDMVM : BaseViewModel
    {
        public SDMVM()
        {
            CauKienItems.Add(new SDMModel());
        }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand UpdateCmd { get; set; }

        private ObservableCollection<SDMModel> _cauKienItems = new ObservableCollection<SDMModel>();
        public ObservableCollection<SDMModel> CauKienItems
        {
            get { return _cauKienItems; }
            set { _cauKienItems = value; OnPropertyChanged(); }
        }

        private string _SH;
        public string SH
        {
            get => _SH;
            set
            {
                _SH = value;
                OnPropertyChanged(nameof(SH));
            }
        }

        private string _STT;
        public string STT
        {
            get => _STT;
            set
            {
                _STT = value;
                OnPropertyChanged(nameof(STT));
            }
        }

        private string _TBV;
        public string TBV
        {
            get => _TBV;
            set
            {
                _TBV = value;
                OnPropertyChanged(nameof(TBV));
            }
        }

      
    }
}
