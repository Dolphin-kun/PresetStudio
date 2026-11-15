using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PresetStudio.View
{
    internal partial class TemplateInputWindow : Window
    {
        public TemplateInputWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (FindFirst<TextBox>(Rows) is { } textBox)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            };
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DialogResult = true;
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private static T? FindFirst<T>(DependencyObject parent) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T { IsVisible: true } match)
                    return match;

                if (FindFirst<T>(child) is { } found)
                    return found;
            }

            return null;
        }
    }
}
