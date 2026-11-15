using System.Windows;
using System.Windows.Media;
using PresetStudio.Commons;
using PresetStudio.Model;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;
using YukkuriMovieMaker.Views;

namespace PresetStudio.View
{
    internal static class TimelineDropHandler
    {
        private static bool isRegistered;

        public static void Register()
        {
            if (isRegistered)
                return;
            isRegistered = true;

            EventManager.RegisterClassHandler(typeof(TimelineView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
            EventManager.RegisterClassHandler(typeof(TimelineView), UIElement.PreviewDragEnterEvent, new DragEventHandler(OnDragOver), true);
            EventManager.RegisterClassHandler(typeof(TimelineView), UIElement.PreviewDragOverEvent, new DragEventHandler(OnDragOver), true);
            EventManager.RegisterClassHandler(typeof(TimelineView), UIElement.PreviewDropEvent, new DragEventHandler(OnDrop), true);
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is UIElement element)
                element.AllowDrop = true;
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (sender is not TimelineView view || PresetDragData.From(e.Data) is not { } data)
                return;

            e.Effects = Resolve(view, e) is { } target && data.CanDrop(target.Item) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private static async void OnDrop(object sender, DragEventArgs e)
        {
            if (sender is not TimelineView view || PresetDragData.From(e.Data) is not { } data)
                return;

            e.Handled = true;
            if (Resolve(view, e) is not { } target || !data.CanDrop(target.Item) || TimelineLocator.Find(view) is not { } timeline)
                return;

            var owner = Window.GetWindow(view);
            owner?.Activate();
            PresetLinkStore.EnsureScope(TimelineLocator.FindScenes(view));
            try
            {
                if (await data.DropAsync(new DropTarget(timeline, target.Item, target.Frame, target.Layer)))
                    TimelineLocator.FindUndoRedoManager(view)?.Record();
            }
            catch
            {
                MessageWindow.Inform(Texts.DropFailed, owner);
            }
        }

        private static (IItem? Item, int Frame, int Layer)? Resolve(TimelineView view, DragEventArgs e)
        {
            var canvas = FindDescendants<FastCanvasItemsControl>(view)
                .FirstOrDefault(control => control.DataContext is TimelineViewModel viewModel && ReferenceEquals(control.Items, viewModel.Items));
            if (canvas?.DataContext is not TimelineViewModel timelineViewModel)
                return null;

            var point = e.GetPosition(canvas);
            if (point.X < 0 || point.Y < 0)
                return null;

            var item = timelineViewModel.Items
                .FirstOrDefault(x => x.Left <= point.X && point.X < x.Left + x.Width && x.Top <= point.Y && point.Y < x.Top + x.Height)?
                .Item;
            var settings = SettingsBase<YMMSettings>.Default;
            var frame = (int)(point.X * 100 / Math.Max(0.0001, settings.TimelineZoom));
            var layer = (int)(point.Y / Math.Max(1, settings.LayerHeight));
            return (item, frame, layer);
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    yield return match;

                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
