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
    public class TKCKVM : BaseViewModel
    {
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand DrawTableCmd { get; set; }

        private ObservableCollection<TKCKModel> _cauKienItems;
        public ObservableCollection<TKCKModel> CauKienItems
        {
            get { return _cauKienItems; }
            set { _cauKienItems = value; OnPropertyChanged(); }
        }

        private string _stt;
        public string STT
        {
            get => _stt;
            set
            {
                _stt = value;
                OnPropertyChanged(nameof(STT));
            }
        }

        private string _tenCauKien;
        public string TenCauKien
        {
            get => _tenCauKien;
            set
            {
                _tenCauKien = value;
                OnPropertyChanged(nameof(TenCauKien));
            }
        }

        private string _cd;
        public string CD
        {
            get => _cd;
            set
            {
                _cd = value;
                OnPropertyChanged(nameof(CD));
            }
        }

        private string _cc;
        public string CC
        {
            get => _cc;
            set
            {
                _cc = value;
                OnPropertyChanged(nameof(CC));
            }
        }

        private string _cr;
        public string CR
        {
            get => _cr;
            set
            {
                _cr = value;
                OnPropertyChanged(nameof(CR));
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
    }
}
