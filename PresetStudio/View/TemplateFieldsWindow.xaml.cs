using System.Windows;

namespace PresetStudio.View
{
    internal partial class TemplateFieldsWindow : Window
    {
        public TemplateFieldsWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
