using System.Windows;
using PresetStudio.Model;

namespace PresetStudio.View
{
    internal partial class TemplateRestoreWindow : Window
    {
        public TemplateRestoreWindow(IReadOnlyList<RestorableTemplate> templates)
        {
            InitializeComponent();
            DataContext = templates;
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
