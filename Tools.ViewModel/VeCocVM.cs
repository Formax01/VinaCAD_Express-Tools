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
    public class VeCocVM : BaseViewModel
    {
        public VeCocVM() 
        {
            CauKienItems.Add(new VeCocModel());
            IsMeter = true;
        }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand VeCocCmd { get; set; }

        private ObservableCollection<VeCocModel> _cauKienItems =  new ObservableCollection<VeCocModel>();
        public ObservableCollection<VeCocModel> CauKienItems
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

        private bool _isMeter;
        public bool IsMeter
        {
            get
            {
                return _isMeter;
            }
            set
            {
                if (_isMeter != value)
                {
                    _isMeter = value;
                    OnPropertyChanged(nameof(IsMeter));
                }
            }
        }

        private bool _isMillimeter;
        public bool IsMillimeter
        {
            get
            {
                return _isMillimeter;
            }
            set
            {
                if (_isMillimeter != value)
                {
                    _isMillimeter = value;
                    OnPropertyChanged(nameof(IsMillimeter));
                }
            }
        }
    }
}
