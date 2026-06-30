using System.Windows;
using System.Windows.Controls;

namespace Tools.View.Behaviors
{
    public static class ComboBoxBehavior
    {
        public static readonly DependencyProperty IsFocusedProperty =
        DependencyProperty.RegisterAttached(
            "IsFocused",
            typeof(bool),
            typeof(ComboBoxBehavior),
            new PropertyMetadata(false, OnIsFocusedChanged));

        public static bool GetIsFocused(DependencyObject obj) => (bool)obj.GetValue(IsFocusedProperty);
        public static void SetIsFocused(DependencyObject obj, bool value) => obj.SetValue(IsFocusedProperty, value);

        private static void OnIsFocusedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboBox comboBox && e.NewValue is bool shouldFocus && shouldFocus)
            {
                comboBox.Loaded += (s, args) =>
                {
                    if (comboBox.SelectedItem == null)
                    {
                        var textBox = (TextBox)comboBox.Template.FindName("PART_EditableTextBox", comboBox);
                        textBox?.Focus();
                    }
                };
            }
        }
    }
}
