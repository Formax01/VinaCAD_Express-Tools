using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tools.Model;
using Tools.ViewModel;

namespace Tools.View.UI
{
    public partial class WindowStylePickerWindow : Window
    {
        private WindowStylePickerVM ViewModel => (WindowStylePickerVM)DataContext;
        public WindowStyleModel? SelectedStyle => ViewModel.SelectedStyle;

        public WindowStylePickerWindow(WindowStyleCatalog catalog, WindowStyleModel? initialStyle = null)
        {
            InitializeComponent();
            DataContext = new WindowStylePickerVM(catalog, initialStyle);
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void StyleList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? element = e.OriginalSource as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = VisualTreeHelper.GetParent(element);
            if (element is not ListBoxItem item || item.DataContext is not WindowStyleModel style) return;
            ViewModel.SelectedStyle = style;
            DialogResult = true;
        }

        private void Previous_Click(object sender, RoutedEventArgs e) => ViewModel.PreviousPage();
        private void Next_Click(object sender, RoutedEventArgs e) => ViewModel.NextPage();
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        }
    }
}
