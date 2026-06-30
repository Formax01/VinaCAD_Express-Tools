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
    public class DrawingCategoriesVM : BaseViewModel
    {
        private ObservableCollection<DrawingCategoriesModel> _drawingItems;
        public ObservableCollection<DrawingCategoriesModel> DrawingItems
        {
            get { return _drawingItems; }
            set { _drawingItems = value; OnPropertyChanged(); }
        }
        public RelayCommand CancelCmd { get; set; }
        public RelayCommand DrawTableCmd { get; set; }

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

        private string _drawingName;
        public string DrawingName
        {
            get => _drawingName;
            set
            {
                _drawingName = value;
                OnPropertyChanged(nameof(DrawingName));
            }
        }

        private string _drawingNo;
        public string DrawingNo
        {
            get => _drawingNo;
            set
            {
                _drawingNo = value;
                OnPropertyChanged(nameof(DrawingNo));
            }
        }

    }
}
