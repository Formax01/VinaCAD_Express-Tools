using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Tools.VinaCad.Modeling;

namespace Tools.ViewModel
{
    public sealed class StuccoSettingsVM : BaseViewModel
    {
        private string _layerName;
        private string _layerColorIndexText;
        private string _thicknessText;
        private Brush _layerColorPreview;
        private readonly Dictionary<string, short> _availableLayers;

        public List<string> LayerNames { get; }

        public string LayerName
        {
            get => _layerName;
            set
            {
                if (_layerName == value) return;
                _layerName = value;
                OnPropertyChanged(nameof(LayerName));

                string normalizedName = value?.Trim() ?? string.Empty;
                if (_availableLayers.TryGetValue(normalizedName, out short colorIndex))
                    LayerColorIndexText = colorIndex.ToString(CultureInfo.InvariantCulture);
            }
        }

        public string LayerColorIndexText
        {
            get => _layerColorIndexText;
            set
            {
                if (_layerColorIndexText == value) return;
                _layerColorIndexText = value;
                LayerColorPreview = TryParseColorIndex(value, out short colorIndex)
                    ? CreateAciBrush(colorIndex)
                    : Brushes.Transparent;
                OnPropertyChanged(nameof(LayerColorIndexText));
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

        public Brush LayerColorPreview
        {
            get => _layerColorPreview;
            private set
            {
                if (ReferenceEquals(_layerColorPreview, value)) return;
                _layerColorPreview = value;
                OnPropertyChanged(nameof(LayerColorPreview));
            }
        }

        public StuccoSettingsVM(StuccoSetting settings, IDictionary<string, short> availableLayers)
        {
            StuccoSetting source = settings ?? new StuccoSetting();
            _availableLayers = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            if (availableLayers != null)
            {
                foreach (KeyValuePair<string, short> layer in availableLayers)
                    _availableLayers[layer.Key] = layer.Value;
            }

            if (!_availableLayers.ContainsKey(source.LayerName))
                _availableLayers[source.LayerName] = source.LayerColorIndex;

            LayerNames = _availableLayers.Keys.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _layerName = source.LayerName;
            _layerColorIndexText = source.LayerColorIndex.ToString(CultureInfo.InvariantCulture);
            _thicknessText = source.Thickness.ToString(CultureInfo.CurrentCulture);
            _layerColorPreview = CreateAciBrush(source.LayerColorIndex);
        }

        public bool TryAccept(out StuccoSetting settings, out string validationMessage)
        {
            settings = new StuccoSetting();
            validationMessage = string.Empty;

            string normalizedLayerName = LayerName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedLayerName))
            {
                validationMessage = "Layer vữa không được để trống.";
                return false;
            }

            if (ContainsInvalidLayerNameCharacter(normalizedLayerName))
            {
                validationMessage = "Tên layer chứa ký tự không hợp lệ: < > / \\ \" : ; ? * | , =";
                return false;
            }

            if (!TryParseColorIndex(LayerColorIndexText, out short colorIndex))
            {
                validationMessage = "Màu Layer phải là chỉ số ACI từ 1 đến 255.";
                return false;
            }

            if (!TryParsePositiveDouble(ThicknessText, out double thickness))
            {
                validationMessage = "Độ dày vữa phải là số hữu hạn lớn hơn 0.";
                return false;
            }

            settings = new StuccoSetting
            {
                LayerName = normalizedLayerName,
                LayerColorIndex = colorIndex,
                Thickness = thickness
            };
            return true;
        }

        public void ResetDefaults()
        {
            StuccoSetting defaults = new StuccoSetting();
            LayerName = defaults.LayerName;
            LayerColorIndexText = defaults.LayerColorIndex.ToString(CultureInfo.InvariantCulture);
            ThicknessText = defaults.Thickness.ToString(CultureInfo.CurrentCulture);
        }

        private static bool ContainsInvalidLayerNameCharacter(string layerName)
        {
            const string invalidCharacters = "<>/\\\":;?*|,=";
            return layerName.IndexOfAny(invalidCharacters.ToCharArray()) >= 0;
        }

        private static bool TryParseColorIndex(string text, out short colorIndex)
        {
            bool parsed = short.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out colorIndex);
            return parsed && colorIndex >= 1 && colorIndex <= 255;
        }

        private static bool TryParsePositiveDouble(string text, out double value)
        {
            string input = text?.Trim() ?? string.Empty;
            bool parsed = double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                          double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static Brush CreateAciBrush(short colorIndex)
        {
            Color color = GetAciPreviewColor(colorIndex);
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Color GetAciPreviewColor(short index)
        {
            Color[] basicColors =
            {
                Colors.Transparent,
                Color.FromRgb(255, 0, 0),
                Color.FromRgb(255, 255, 0),
                Color.FromRgb(0, 255, 0),
                Color.FromRgb(0, 255, 255),
                Color.FromRgb(0, 0, 255),
                Color.FromRgb(255, 0, 255),
                Color.FromRgb(255, 255, 255),
                Color.FromRgb(128, 128, 128),
                Color.FromRgb(192, 192, 192)
            };

            if (index >= 1 && index <= 9)
                return basicColors[index];

            if (index >= 250)
            {
                byte[] grayValues = { 51, 91, 132, 173, 214, 255 };
                byte gray = grayValues[index - 250];
                return Color.FromRgb(gray, gray, gray);
            }

            int group = index / 10;
            int shade = index % 10;
            double hue = (group - 1) * 15.0;
            double saturation = shade % 2 == 0 ? 1.0 : 0.5;
            double value = shade switch
            {
                0 or 1 => 1.0,
                2 or 3 => 0.8,
                4 or 5 => 0.6,
                6 or 7 => 0.5,
                _ => 0.3
            };

            return HsvToColor(hue, saturation, value);
        }

        private static Color HsvToColor(double hue, double saturation, double value)
        {
            double chroma = value * saturation;
            double sector = hue / 60.0;
            double intermediate = chroma * (1.0 - Math.Abs(sector % 2.0 - 1.0));
            double r = 0.0;
            double g = 0.0;
            double b = 0.0;

            if (sector < 1) { r = chroma; g = intermediate; }
            else if (sector < 2) { r = intermediate; g = chroma; }
            else if (sector < 3) { g = chroma; b = intermediate; }
            else if (sector < 4) { g = intermediate; b = chroma; }
            else if (sector < 5) { r = intermediate; b = chroma; }
            else { r = chroma; b = intermediate; }

            double match = value - chroma;
            return Color.FromRgb(
                (byte)Math.Round((r + match) * 255.0),
                (byte)Math.Round((g + match) * 255.0),
                (byte)Math.Round((b + match) * 255.0));
        }
    }
}
