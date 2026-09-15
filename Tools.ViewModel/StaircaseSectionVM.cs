using PrMVVMCore;
using Tools.Model;

namespace Tools.ViewModel
{
    public enum StaircaseMeasurement
    {
        None,
        StoreyHeight,
        Landing1Width,
        Landing2Width
    }

    public sealed class StaircaseSectionVM : BaseViewModel
    {
        private StaircaseSectionModel _settings;

        public StaircaseSectionVM(StaircaseSectionModel settings)
        {
            _settings = settings;
        }

        public RelayCommand? AcceptCmd { get; set; }
        public RelayCommand? CancelCmd { get; set; }
        public RelayCommand? ResetCmd { get; set; }
        public RelayCommand? MeasureStoreyHeightCmd { get; set; }
        public RelayCommand? MeasureLanding1WidthCmd { get; set; }
        public RelayCommand? MeasureLanding2WidthCmd { get; set; }

        public StaircaseMeasurement MeasurementRequest { get; set; }
        public bool IsAccepted { get; set; }

        public StaircaseSectionModel Settings => _settings;

        public bool IsDoubleFlight
        {
            get => _settings.Type == StaircaseSectionType.DoubleFlight;
            set { if (value) SetType(StaircaseSectionType.DoubleFlight); }
        }

        public bool IsSingleFlight
        {
            get => _settings.Type == StaircaseSectionType.SingleFlight;
            set { if (value) SetType(StaircaseSectionType.SingleFlight); }
        }

        public bool IsScissor
        {
            get => _settings.Type == StaircaseSectionType.Scissor;
            set { if (value) SetType(StaircaseSectionType.Scissor); }
        }

        public bool FirstRunRightward
        {
            get => _settings.FirstRunRightward;
            set
            {
                if (_settings.FirstRunRightward == value) return;
                _settings.FirstRunRightward = value;
                OnPropertyChanged(nameof(FirstRunRightward));
                OnPropertyChanged(nameof(FirstRunLeftward));
            }
        }

        public bool FirstRunLeftward
        {
            get => !_settings.FirstRunRightward;
            set { if (value) FirstRunRightward = false; }
        }

        public int StoreyNumber
        {
            get => _settings.StoreyNumber;
            set { _settings.StoreyNumber = value; OnPropertyChanged(nameof(StoreyNumber)); }
        }

        public double StoreyHeight
        {
            get => _settings.StoreyHeight;
            set
            {
                _settings.StoreyHeight = value;
                OnPropertyChanged(nameof(StoreyHeight));
                OnPropertyChanged(nameof(CurrentStepHeight));
            }
        }

        public double TreadRun
        {
            get => _settings.TreadRun;
            set { _settings.TreadRun = value; OnPropertyChanged(nameof(TreadRun)); }
        }

        public int StepNumber
        {
            get => _settings.StepNumber;
            set
            {
                _settings.StepNumber = value;
                OnPropertyChanged(nameof(StepNumber));
                OnPropertyChanged(nameof(CurrentStepHeight));
            }
        }

        public int FirstFlightStepNumber
        {
            get => _settings.FirstFlightStepNumber;
            set { _settings.FirstFlightStepNumber = value; OnPropertyChanged(nameof(FirstFlightStepNumber)); }
        }

        public double Landing1Width
        {
            get => _settings.Landing1Width;
            set { _settings.Landing1Width = value; OnPropertyChanged(nameof(Landing1Width)); }
        }

        public double Landing2Width
        {
            get => _settings.Landing2Width;
            set { _settings.Landing2Width = value; OnPropertyChanged(nameof(Landing2Width)); }
        }

        public double GirderHeight
        {
            get => _settings.GirderHeight;
            set { _settings.GirderHeight = value; OnPropertyChanged(nameof(GirderHeight)); }
        }

        public double GirderWidth
        {
            get => _settings.GirderWidth;
            set { _settings.GirderWidth = value; OnPropertyChanged(nameof(GirderWidth)); }
        }

        public double BeamHeight
        {
            get => _settings.BeamHeight;
            set { _settings.BeamHeight = value; OnPropertyChanged(nameof(BeamHeight)); }
        }

        public double BeamWidth
        {
            get => _settings.BeamWidth;
            set { _settings.BeamWidth = value; OnPropertyChanged(nameof(BeamWidth)); }
        }

        public bool HasBeam1
        {
            get => _settings.HasBeam1;
            set { _settings.HasBeam1 = value; OnPropertyChanged(nameof(HasBeam1)); }
        }

        public bool HasBeam2
        {
            get => _settings.HasBeam2;
            set { _settings.HasBeam2 = value; OnPropertyChanged(nameof(HasBeam2)); }
        }

        public double BoardThickness
        {
            get => _settings.BoardThickness;
            set { _settings.BoardThickness = value; OnPropertyChanged(nameof(BoardThickness)); }
        }

        public double RailingHeight
        {
            get => _settings.RailingHeight;
            set { _settings.RailingHeight = value; OnPropertyChanged(nameof(RailingHeight)); }
        }

        public bool CreateGroup
        {
            get => _settings.CreateGroup;
            set { _settings.CreateGroup = value; OnPropertyChanged(nameof(CreateGroup)); }
        }

        public double CurrentStepHeight => _settings.CurrentStepHeight;

        public void Reset(StaircaseSectionModel defaults)
        {
            _settings = defaults;
            OnPropertyChanged(string.Empty);
        }

        private void SetType(StaircaseSectionType type)
        {
            if (_settings.Type == type) return;
            _settings.Type = type;
            OnPropertyChanged(nameof(IsDoubleFlight));
            OnPropertyChanged(nameof(IsSingleFlight));
            OnPropertyChanged(nameof(IsScissor));
        }
    }
}
