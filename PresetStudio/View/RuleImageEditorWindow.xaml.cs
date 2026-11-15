using System.Windows;
using System.Windows.Input;
using PresetStudio.ViewModel;

namespace PresetStudio.View
{
    internal partial class RuleImageEditorWindow : Window
    {
        public RuleImageEditorWindow(RuleImageEditorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RuleImageEditorViewModel { CanSubmit: true })
                DialogResult = true;
        }
    }
}
