using System;
using System.Windows;
using Tools.Model;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tools.VinaCAD.UI
{
    public partial class GridAxisWindow : Window
    {
        public GridAxisInput? Input { get; set; }
        public Canvas PreviewCanvas => PreviewCanvasControl;
        public TextBox BreadthsTextBox => BreadthsTextBoxControl;

        public event EventHandler? ResetRequested;
        public event EventHandler? OkRequested;
        public event EventHandler<SizeChangedEventArgs>? PreviewSizeChanged;

        public GridAxisWindow()
        {
            InitializeComponent();
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
            => ResetRequested?.Invoke(this, EventArgs.Empty);

        private void OkButton_Click(object sender, RoutedEventArgs e)
            => OkRequested?.Invoke(this, EventArgs.Empty);

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => PreviewSizeChanged?.Invoke(this, e);
    }
}
