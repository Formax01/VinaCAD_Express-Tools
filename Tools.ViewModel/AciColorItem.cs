using System.Windows.Media;
using System.ComponentModel;

namespace Tools.ViewModel
{
    public sealed class AciColorItem : INotifyPropertyChanged
    {
        public short Index { get; }
        public Brush ColorBrush { get; }
        public string Description { get; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public AciColorItem(short index, Brush colorBrush, string description)
        {
            Index = index;
            ColorBrush = colorBrush;
            Description = description;
        }
    }
}
