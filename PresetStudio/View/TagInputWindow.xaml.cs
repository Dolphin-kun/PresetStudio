using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PresetStudio.ViewModel;

namespace PresetStudio.View
{
    internal partial class TagInputWindow : Window
    {
        private readonly TagInputViewModel viewModel;
        private bool isAccepting;

        public TagInputWindow(TagInputViewModel viewModel)
        {
            InitializeComponent();
            DataContext = this.viewModel = viewModel;
            Loaded += (_, _) => TagBox.CaretIndex = TagBox.Text.Length;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            var isOpen = viewModel.IsSuggestionOpen && SuggestionList.Items.Count > 0;
            switch (e.Key)
            {
                case Key.Escape:
                    if (isOpen)
                        viewModel.IsSuggestionOpen = false;
                    else
                        Close();
                    e.Handled = true;
                    return;
                case Key.Down:
                    if (isOpen)
                        MoveSelection(1);
                    else
                        viewModel.UpdateSuggestions(TagBox.CaretIndex, showAll: true);
                    e.Handled = true;
                    return;
                case Key.Up when isOpen:
                    MoveSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Tab when isOpen:
                    Accept(SuggestionList.SelectedItem as string ?? (string)SuggestionList.Items[0]);
                    e.Handled = true;
                    return;
                case Key.Enter when isOpen && SuggestionList.SelectedItem is string tag:
                    Accept(tag);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    DialogResult = true;
                    e.Handled = true;
                    return;
            }

            base.OnPreviewKeyDown(e);
        }

        private void MoveSelection(int delta)
        {
            var count = SuggestionList.Items.Count;
            SuggestionList.SelectedIndex = SuggestionList.SelectedIndex < 0
                ? (delta > 0 ? 0 : count - 1)
                : (SuggestionList.SelectedIndex + delta + count) % count;
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }

        private void Accept(string tag)
        {
            isAccepting = true;
            try
            {
                var caret = viewModel.Accept(tag, TagBox.CaretIndex);
                TagBox.CaretIndex = caret;
            }
            finally
            {
                isAccepting = false;
            }

            TagBox.Focus();
        }

        private void TagBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isAccepting || !TagBox.IsKeyboardFocusWithin)
                return;

            viewModel.UpdateSuggestions(TagBox.CaretIndex);
            SuggestionList.SelectedIndex = -1;
        }

        private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            for (var element = e.OriginalSource as DependencyObject; element is not null && element != SuggestionList; element = VisualTreeHelper.GetParent(element))
            {
                if (element is ListBoxItem { DataContext: string tag })
                {
                    Accept(tag);
                    e.Handled = true;
                    return;
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
