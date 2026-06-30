using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.ViewModel
{
    public class XLTKCKVM : BaseViewModel
    {
        public XLTKCKVM()
        {
            CheckDistance=50.0;
            Scale = "1/100";
        }
        private string _textHeight;
        public string TextHeight
        {
            get
            {
                return _textHeight;
            }
            set
            {
                if (_textHeight != value)
                {
                    _textHeight = value;
                    OnPropertyChanged(nameof(TextHeight));
                }
            }
        }

        private string _textLayer;
        public string TextLayer
        {
            get
            {
                return _textLayer;
            }
            set
            {
                if (_textLayer != value)
                {
                    _textLayer = value;
                    OnPropertyChanged(nameof(TextLayer));
                }
            }
        }

        private double _checkDistance;
        public double CheckDistance
        {
            get
            {
                return _checkDistance;
            }
            set
            {
                if (_checkDistance != value)
                {
                    _checkDistance = value;
                    OnPropertyChanged(nameof(CheckDistance));
                }
            }
        }

        private string _scale;
        public string Scale
        {
            get
            {
                return _scale;
            }
            set
            {
                if (_scale != value)
                {
                    _scale = value;
                    OnPropertyChanged(nameof(Scale));
                }
            }
        }

        public RelayCommand OkCmd { get; set; }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand PickTextCmd { get; set; }
    }
}
