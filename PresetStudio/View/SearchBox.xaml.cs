using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PresetStudio.View
{
    public partial class SearchBox : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SearchBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public SearchBox()
        {
            InitializeComponent();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            Text = string.Empty;
            e.Handled = true;
        }
    }
}
