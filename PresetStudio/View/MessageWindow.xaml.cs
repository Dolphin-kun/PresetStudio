using System.Windows;
using System.Windows.Input;

namespace PresetStudio.View
{
    internal partial class MessageWindow : Window
    {
        private MessageWindow(string message, bool isConfirm)
        {
            InitializeComponent();
            MessageText.Text = message;
            if (!isConfirm)
                CancelButton.Visibility = Visibility.Collapsed;

            Loaded += (_, _) => OkButton.Focus();
        }

        public static bool Confirm(string message, Window? owner = null) => Show(message, owner, isConfirm: true);

        public static void Inform(string message, Window? owner = null) => Show(message, owner, isConfirm: false);

        private static bool Show(string message, Window? owner, bool isConfirm)
        {
            var window = new MessageWindow(message, isConfirm)
            {
                Owner = owner ?? Application.Current?.MainWindow,
            };

            return window.ShowDialog() == true;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    DialogResult = true;
                    e.Handled = true;
                    return;
            }

            base.OnPreviewKeyDown(e);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
