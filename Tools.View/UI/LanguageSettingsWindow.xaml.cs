using System;
using System.Windows;
using Tools.Resources.Language;

namespace Tools.View.UI
{
    public partial class LanguageSettingsWindow : Window
    {
        public LanguageSettingsWindow()
        {
            InitializeComponent();
            LanguageList.ItemsSource = Translator.Supported;
            LanguageList.SelectedItem = Translator.Instance.Current;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (LanguageList.SelectedItem is not LanguageInfo language) return;

            try
            {
                Translator.Instance.SetLanguage(language.Code);

                DialogResult = true;
                MessageBox.Show(string.Format(Lang.Msg_LanguageChanged, Translator.Instance.Current.DisplayName),Lang.Common_Text_ThongBao);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Lang.Common_Text_Loi, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
