using System.Windows;

namespace Tools.View.Behaviors
{
    public static class WindowDialogBehavior
    {
        public static readonly DependencyProperty DialogResultProperty =
            DependencyProperty.RegisterAttached(
                "DialogResult",
                typeof(bool?),
                typeof(WindowDialogBehavior),
                new PropertyMetadata(null, OnDialogResultChanged));

        public static void SetDialogResult(DependencyObject element, bool? value) =>
            element.SetValue(DialogResultProperty, value);

        public static bool? GetDialogResult(DependencyObject element) =>
            (bool?)element.GetValue(DialogResultProperty);

        private static void OnDialogResultChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        {
            if (element is Window window && args.NewValue is bool result && window.IsVisible)
                window.DialogResult = result;
        }
    }
}
