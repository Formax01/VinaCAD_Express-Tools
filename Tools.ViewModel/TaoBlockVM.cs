using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class TaoBlockVM : BaseViewModel
    {
        private string _selectedBlockName;
        public string SelectedBlockName
        {
            get => _selectedBlockName;
            set
            {
                _selectedBlockName = value;
                OnPropertyChanged(nameof(SelectedBlockName));
            }
        }

        private string _newBlockName;
        public string NewBlockName
        {
            get => _newBlockName;
            set
            {
                _newBlockName = value;
                OnPropertyChanged(nameof(NewBlockName));
            }
        }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand TaoBlockCmd { get; set; }
    }
}
