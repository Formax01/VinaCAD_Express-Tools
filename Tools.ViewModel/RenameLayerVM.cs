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
    public class RenameLayerVM : BaseViewModel
    {
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand OkCmd { get; set; }

        private ObservableCollection<RenameLayerModel> _cauKienItems;
        public ObservableCollection<RenameLayerModel> CauKienItems
        {
            get { return _cauKienItems; }
            set { _cauKienItems = value; OnPropertyChanged(); }
        }

        private string _nameLayer;
        public string NameLayer
        {
            get => _nameLayer;
            set
            {
                _nameLayer = value;
                OnPropertyChanged(nameof(NameLayer));
            }
        }

        private string _renameLayer;
        public string RenameLayer
        {
            get => _renameLayer;
            set
            {
                _renameLayer = value;
                OnPropertyChanged(nameof(RenameLayer));
            }
        }

    }
}
