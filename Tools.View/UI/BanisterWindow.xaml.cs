using System.Windows;
using System.Windows.Controls;
using Tools.Resources.Language;
namespace Tools.View.UI
{
    public partial class BanisterWindow : Window
    {
        public BanisterWindow()
        {
            InitializeComponent();
        }

        public Canvas SlopedPreviewCanvas => SlopedCanvas;
        public Canvas FlatPreviewCanvas => FlatCanvas;
    }
}
