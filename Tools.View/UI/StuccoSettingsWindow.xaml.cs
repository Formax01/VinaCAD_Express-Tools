using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Tools.ViewModel;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCAD.UI
{
    public partial class StuccoSettingsWindow : Window
    {
        private static readonly Regex IntegerInputRegex = new Regex("^[0-9]+$");
        private static readonly Regex DecimalInputRegex = new Regex("^[0-9.,]+$");

        private StuccoSettingsVM ViewModel => (StuccoSettingsVM)DataContext;

        public StuccoSetting? AcceptedSettings { get; private set; }

        public StuccoSettingsWindow(StuccoSetting settings, IDictionary<string, short> availableLayers)
        {
            InitializeComponent();
            DataContext = new StuccoSettingsVM(settings, availableLayers);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.TryAccept(out StuccoSetting settings, out string validationMessage))
            {
                AcceptedSettings = settings;
                DialogResult = true;
                return;
            }

            MessageBox.Show(validationMessage, "FN - Dữ liệu không hợp lệ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetDefaults();
            CmbLayerName.Focus();
        }

        private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IntegerInputRegex.IsMatch(e.Text);
        }

        private void DecimalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DecimalInputRegex.IsMatch(e.Text);
        }

    }
}
