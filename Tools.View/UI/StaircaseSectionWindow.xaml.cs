using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tools.View.UI
{
    public partial class StaircaseSectionWindow : Window
    {
        public StaircaseSectionWindow()
        {
            InitializeComponent();
        }

        public bool HasValidationErrors()
        {
            return HasValidationErrors(this);
        }

        private static bool HasValidationErrors(DependencyObject element)
        {
            if (Validation.GetHasError(element)) return true;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            {
                if (HasValidationErrors(VisualTreeHelper.GetChild(element, index))) return true;
            }
            return false;
        }
    }
}
