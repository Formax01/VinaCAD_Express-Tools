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
    public class EditTKCTHVM : BaseViewModel
    {
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand UpdateCmd { get; set; }

        private ObservableCollection<TKCTHModel> _thepHinhItems;
        public ObservableCollection<TKCTHModel> ThepHinhItems
        {
            get { return _thepHinhItems; }
            set { _thepHinhItems = value; OnPropertyChanged(); }
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

        private string _qct;
        public string QCT
        {
            get => _qct;
            set
            {
                _qct = value;
                OnPropertyChanged(nameof(QCT));
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

    }
}
