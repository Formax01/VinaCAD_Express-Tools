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
    public class TKCTVM : BaseViewModel
    {
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand UpdateCmd { get; set; }

        private ObservableCollection<TKCTModel> _cotThepItems;
        public ObservableCollection<TKCTModel> CotThepItems
        {
            get { return _cotThepItems; }
            set { _cotThepItems = value; OnPropertyChanged(); }
        }

        private string _soHieu;
        public string SoHieu
        {
            get => _soHieu;
            set
            {
                _soHieu = value;
                OnPropertyChanged(nameof(SoHieu));
            }
        }

        private string _l1;
        public string L1
        {
            get => _l1;
            set
            {
                _l1 = value;
                OnPropertyChanged(nameof(L1));
            }
        }

        private string _l2;
        public string L2
        {
            get => _l2;
            set
            {
                _l2 = value;
                OnPropertyChanged(nameof(L2));
            }
        }

        private string _l3;
        public string L3
        {
            get => _l3;
            set
            {
                _l3 = value;
                OnPropertyChanged(nameof(L3));
            }
        }

        private string _d;
        public string D
        {
            get => _d;
            set
            {
                _d = value;
                OnPropertyChanged(nameof(D));
            }
        }

        private string _sl;
        public string SL
        {
            get => _sl;
            set
            {
                _sl = value;
                OnPropertyChanged(nameof(SL));
            }
        }
        
        private string _sck;
        public string SCK
        {
            get => _sck;
            set
            {
                _sck = value;
                OnPropertyChanged(nameof(SCK));
            }
        }
        

        private string _l4;
        public string L4
        {
            get => _l4;
            set
            {
                _l4 = value;
                OnPropertyChanged(nameof(L4));
            }
        }
        private string _l5;
        public string L5
        {
            get => _l5;
            set
            {
                _l5 = value;
                OnPropertyChanged(nameof(L5));
            }
        }
       
    }
}
