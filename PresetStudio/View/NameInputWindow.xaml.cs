using System.Windows;
using System.Windows.Input;
using PresetStudio.ViewModel;

namespace PresetStudio.View
{
    internal partial class NameInputWindow : Window
    {
        public NameInputWindow(NameInputViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            if (viewModel.IsCategoryEditable)
                Width = 360;

            Loaded += (_, _) =>
            {
                if (viewModel.HasNameSuggestions)
                {
                    NameBox.Focus();
                    return;
                }

                NameTextBox.Focus();
                NameTextBox.SelectAll();
            };
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    return;
                case Key.Enter when !NameBox.IsDropDownOpen && !CategoryBox.IsDropDownOpen:
                    Submit();
                    e.Handled = true;
                    return;
            }

            base.OnPreviewKeyDown(e);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Submit();

        private void Submit()
        {
            if (DataContext is NameInputViewModel { CanSubmit: true })
                DialogResult = true;
        }
    }
}
