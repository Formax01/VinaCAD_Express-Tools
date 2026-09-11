using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public class GridAxisVM : BaseViewModel
    {
        private string _breadthsText;
        private string _depthsText;
        private bool _drawAnnotations;
        private string _validationMessage;

        public string BreadthsText
        {
            get => _breadthsText;
            set
            {
                if (_breadthsText == value) return;
                _breadthsText = value;
                OnPropertyChanged(nameof(BreadthsText));
                RefreshValidation();
            }
        }

        public string DepthsText
        {
            get => _depthsText;
            set
            {
                if (_depthsText == value) return;
                _depthsText = value;
                OnPropertyChanged(nameof(DepthsText));
                RefreshValidation();
            }
        }

        public bool DrawAnnotations
        {
            get => _drawAnnotations;
            set
            {
                if (_drawAnnotations == value) return;
                _drawAnnotations = value;
                OnPropertyChanged(nameof(DrawAnnotations));
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (_validationMessage == value) return;
                _validationMessage = value;
                OnPropertyChanged(nameof(ValidationMessage));
            }
        }

        public bool IsValid => TryBuildInput(out _, out _);

        public GridAxisVM(
            string? breadthsText = null,
            string? depthsText = null,
            bool? drawAnnotations = null)
        {
            _breadthsText = string.IsNullOrWhiteSpace(breadthsText)
                ? GridAxisSetting.DefaultBreadths
                : breadthsText;
            _depthsText = string.IsNullOrWhiteSpace(depthsText)
                ? GridAxisSetting.DefaultDepths
                : depthsText;
            _drawAnnotations = drawAnnotations ?? GridAxisSetting.DefaultDrawAnnotations;
            _validationMessage = string.Empty;
            RefreshValidation();
        }

        public void ResetDefaults()
        {
            BreadthsText = GridAxisSetting.DefaultBreadths;
            DepthsText = GridAxisSetting.DefaultDepths;
            DrawAnnotations = GridAxisSetting.DefaultDrawAnnotations;
        }

        public bool TryGetPreview(out IReadOnlyList<double> breadths, out IReadOnlyList<double> depths)
        {
            bool breadthsOk = GridAxisDataHelper.TryParseSpacings(BreadthsText, "Breadths", out breadths, out _);
            bool depthsOk = GridAxisDataHelper.TryParseSpacings(DepthsText, "Depths", out depths, out _);
            if (depthsOk)
                depths = depths.Reverse().ToArray();
            return breadthsOk && depthsOk;
        }

        public bool TryBuildInput(out GridAxisInput? input, out string error)
        {
            input = null;

            if (!GridAxisDataHelper.TryParseSpacings(BreadthsText, "Breadths", out IReadOnlyList<double> breadths, out error))
                return false;

            if (!GridAxisDataHelper.TryParseSpacings(DepthsText, "Depths", out IReadOnlyList<double> depths, out error))
                return false;

            input = new GridAxisInput(breadths, depths.Reverse(), DrawAnnotations);
            error = string.Empty;
            return true;
        }

        private void RefreshValidation()
        {
            TryBuildInput(out _, out string error);
            ValidationMessage = error;
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
