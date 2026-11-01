using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Model;

namespace Tools.ViewModel
{
    public class FlexDuctVM : BaseViewModel
    {
        private FlexDuctModel _model;

        public FlexDuctVM()
        {
            _model = new FlexDuctModel();
        }

        public double Diameter
        {
            get => _model.Diameter;
            set
            {
                if (_model.Diameter != value)
                {
                    _model.Diameter = value;
                    OnPropertyChanged(nameof(Diameter));
                }
            }
        }

        public FlexDuctType DuctType
        {
            get => _model.DuctType;
            set
            {
                if (_model.DuctType == value) return;
                _model.DuctType = value;
                OnPropertyChanged(nameof(DuctType));
            }
        }

        public FlexDuctModel Model => _model;

        public FlexDuctPathMode PathMode
        {
            get => _model.PathMode;
            set
            {
                if (_model.PathMode == value) return;
                _model.PathMode = value;
                OnPropertyChanged(nameof(PathMode));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
