using System;
using System.Windows;
using Tools.Model;
using System.Windows.Controls;

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
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
            => ResetRequested?.Invoke(this, EventArgs.Empty);

        private void OkButton_Click(object sender, RoutedEventArgs e)
            => OkRequested?.Invoke(this, EventArgs.Empty);

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => PreviewSizeChanged?.Invoke(this, e);
    }
}
