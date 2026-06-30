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
    public class RenameTextVM : BaseViewModel
    {

        public RenameTextVM()
        {
            IsOverride = true;
        }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand OkCmd { get; set; }

        private ObservableCollection<RenameTextModel> _cauKienItems;
        public ObservableCollection<RenameTextModel> CauKienItems
        {
            get { return _cauKienItems; }
            set { _cauKienItems = value; OnPropertyChanged(); }
        }

        private string _text;
        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged(nameof(Text));
            }
        }

        private string _newText;
        public string NewText
        {
            get => _newText;
            set
            {
                _newText = value;
                OnPropertyChanged(nameof(NewText));
            }
        }

        private bool _isOverride;
        public bool IsOverride
        {
            get
            {
                return _isOverride;
            }
            set
            {
                if (_isOverride != value)
                {
                    _isOverride = value;
                    OnPropertyChanged(nameof(IsOverride));
                }
            }
        }

        private bool _isIncludingOldAndNew;
        public bool IsIncludingOldAndNew
        {
            get
            {
                return _isIncludingOldAndNew;
            }
            set
            {
                if (_isIncludingOldAndNew != value)
                {
                    _isIncludingOldAndNew = value;
                    OnPropertyChanged(nameof(IsIncludingOldAndNew));
                }
            }
        }
    }
}
