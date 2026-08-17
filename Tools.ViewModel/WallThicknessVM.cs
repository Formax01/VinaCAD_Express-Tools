using PrMVVMCore;
using System.Collections.Generic;
using System.Globalization;

namespace Tools.ViewModel
{
    public class WallThicknessVM : BaseViewModel
    {
        private double _selectedThickness;
        private string _thicknessText;
        private bool _isPickUpRequested;

        public IReadOnlyList<double> Thicknesses { get; } = new List<double>
        {
            50, 60, 90, 100, 120, 140, 150, 180, 200, 240, 250, 300, 360, 480
        };

        public double SelectedThickness
        {
            get => _selectedThickness;
            set
            {
                if (_selectedThickness == value) return;
                _selectedThickness = value;
                ThicknessText = value.ToString(CultureInfo.CurrentCulture);
                OnPropertyChanged(nameof(SelectedThickness));
            }
        }

        public string ThicknessText
        {
            get => _thicknessText;
            set
            {
                if (_thicknessText == value) return;
                _thicknessText = value;
                OnPropertyChanged(nameof(ThicknessText));
            }
        }

        public bool IsPickUpRequested
        {
            get => _isPickUpRequested;
            private set
            {
                if (_isPickUpRequested == value) return;
                _isPickUpRequested = value;
                OnPropertyChanged(nameof(IsPickUpRequested));
            }
        }

        public WallThicknessVM(double currentThickness)
        {
            _selectedThickness = currentThickness;
            _thicknessText = currentThickness.ToString(CultureInfo.CurrentCulture);
        }

        public bool TryAcceptThickness()
        {
            if (!double.TryParse(ThicknessText, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) || value <= 0)
                return false;

            SelectedThickness = value;
            return true;
        }

        public void RequestPickUp()
        {
            IsPickUpRequested = true;
        }
    }
}
