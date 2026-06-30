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
    public class BangToaDoCocVM : BaseViewModel
    {

        public RelayCommand CancelCmd { get; set; }
        public RelayCommand DrawTableCmd { get; set; }

        private ObservableCollection<ToaDoCocModel> _cauKienItems;
        public ObservableCollection<ToaDoCocModel> CauKienItems
        {
            get { return _cauKienItems; }
            set { _cauKienItems = value; OnPropertyChanged(); }
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

        private string _kichThuoc;
        public string KichThuoc
        {
            get => _kichThuoc;
            set
            {
                _kichThuoc = value;
                OnPropertyChanged(nameof(KichThuoc));
            }
        }

        private string _toaDoX;
        public string ToaDoX
        {
            get => _toaDoX;
            set
            {
                _toaDoX = value;
                OnPropertyChanged(nameof(ToaDoX));
            }
        }

        private string _toaDoY;
        public string ToaDoY
        {
            get => _toaDoY;
            set
            {
                _toaDoY = value;
                OnPropertyChanged(nameof(ToaDoY));
            }
        }

        private string _ghiChu;
        public string GhiChu
        {
            get => _ghiChu;
            set
            {
                _ghiChu = value;
                OnPropertyChanged(nameof(GhiChu));
            }
        }

    }
}
