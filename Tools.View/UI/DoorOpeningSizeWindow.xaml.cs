using System.Windows;

namespace Tools.View.UI
{
    public partial class DoorOpeningSizeWindow : Window
    {
        public string Request { get; set; } = string.Empty;

        public DoorOpeningSizeWindow()
        {
            InitializeComponent();
        }
    }
}
