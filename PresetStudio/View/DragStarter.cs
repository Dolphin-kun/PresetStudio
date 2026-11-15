using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PresetStudio.Model;

namespace PresetStudio.View
{
    internal sealed class DragStarter
    {
        private readonly UIElement host;
        private readonly Func<object, PresetDragData?> createData;
        private Point start;
        private object? pressed;

        public bool IsDragging { get; private set; }

        public event Action? DragFinished;

        public DragStarter(UIElement host, Func<object, PresetDragData?> createData)
        {
            this.host = host;
            this.createData = createData;
            host.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseDown), true);
            host.AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnMouseMove), true);
            host.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => pressed = null), true);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            pressed = e.ClickCount == 1 ? FindEntry(e.OriginalSource as DependencyObject) : null;
            start = e.GetPosition(host);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (pressed is null || IsDragging)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                pressed = null;
                return;
            }

            var delta = e.GetPosition(host) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var entry = pressed;
            pressed = null;
            if (createData(entry) is not { } data)
                return;

            IsDragging = true;
            try
            {
                DragDrop.DoDragDrop(host, data.ToDataObject(), DragDropEffects.Copy);
            }
            finally
            {
                IsDragging = false;
                DragFinished?.Invoke();
            }
        }

        public static object? FindEntry(DependencyObject? source)
        {
            for (var element = source; element is not null; element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
            {
                switch (element)
                {
                    case ButtonBase or TextBoxBase or ScrollBar or Thumb:
                        return null;
                    case ListBoxItem item:
                        return item.DataContext;
                }
            }

            return null;
        }
    }
}
