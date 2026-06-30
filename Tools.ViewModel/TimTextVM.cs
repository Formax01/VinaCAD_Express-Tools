using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.TextFormatting;

namespace Tools.ViewModel
{
    public class TimTextVM : BaseViewModel
    {
        public RelayCommand SelectAllCmd { get; set; }
        public RelayCommand ScanSelectCmd { get; set; }

        public TimTextVM()
        {
            IsExactMatch = true;
        }

        private string _textFind;
        public string TextFind
        {
            get
            {
                return _textFind;
            }
            set
            {
                if (_textFind != value)
                {
                    _textFind = value;
                    OnPropertyChanged(nameof(TextFind));
                }
            }
        }

        private bool _isExactMatch;
        public bool IsExactMatch
        {
            get
            {
                return _isExactMatch;
            }
            set
            {
                if (_isExactMatch != value)
                {
                    _isExactMatch = value;
                    OnPropertyChanged(nameof(IsExactMatch));

                    if (value)
                    {
                        IsContainsMatch = false;
                    }
                    else if (!IsContainsMatch)
                    {
                        IsContainsMatch = true;
                    }
                }
            }
        }

        private bool _isContainsMatch;
        public bool IsContainsMatch
        {
            get
            {
                return _isContainsMatch;
            }
            set
            {
                if (_isContainsMatch != value)
                {
                    _isContainsMatch = value;
                    OnPropertyChanged(nameof(IsContainsMatch));

                    if (value)
                    {
                        IsExactMatch = false;
                    }
                    else if (!IsExactMatch)
                    {
                        IsExactMatch = true;
                    }
                }
            }
        }

       
    }
}
