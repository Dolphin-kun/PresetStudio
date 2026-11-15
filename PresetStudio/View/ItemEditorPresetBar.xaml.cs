using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PresetStudio.Model;
using PresetStudio.Settings;

namespace PresetStudio.View
{
    internal partial class ItemEditorPresetBar : UserControl
    {
        public event Action? AddRequested;

        public event Action? SplitRequested;

        public event Action<MouseWheelEventArgs>? WheelPassed;

        public event Action<bool>? ShowFoldersChanged;

        public event Action? GoUpRequested;

        public event Action<FolderEntry>? FolderClicked;

        public event Action<TextStylePreset>? PresetClicked;

        public event Action<TextStylePreset, TextStyleParts>? PartApplyRequested;

        public event Action<TextStylePreset>? OverwriteRequested;

        public event Action<TextStylePreset>? DuplicateRequested;

        public event Action<TextStylePreset>? PreviewTextChangeRequested;

        public event Action<TextStylePreset, int>? VariantRequested;

        public event Action<IReadOnlyList<TextStylePreset>>? RealizedPresetsChanged;

        public IEnumerable? ItemsSource
        {
            get => PresetList.ItemsSource;
            set => PresetList.ItemsSource = value;
        }

        public ItemEditorPresetBar()
        {
            InitializeComponent();
            KeepFontToggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PresetStudioSettings.KeepFont)) { Source = PresetStudioSettings.Default, Mode = BindingMode.TwoWay });
            KeepFontSizeToggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PresetStudioSettings.KeepFontSize)) { Source = PresetStudioSettings.Default, Mode = BindingMode.TwoWay });
            ((INotifyCollectionChanged)PresetList.Items).CollectionChanged += (_, _) => ScheduleReport();
            PresetList.AddHandler(PreviewMouseMoveEvent, new MouseEventHandler(PresetList_PreviewMouseMove), true);
            IsVisibleChanged += (_, _) => ScheduleReport();
        }

        public void SetFolderState(bool showFolders, string folder)
        {
            FolderToggle.IsChecked = showFolders;
            FolderToggle.ToolTip = showFolders ? Texts.HideFolders : Texts.ShowFolders;
            FolderShownIcon.Visibility = showFolders ? Visibility.Collapsed : Visibility.Visible;
            FolderHiddenIcon.Visibility = showFolders ? Visibility.Visible : Visibility.Collapsed;
            UpButton.Visibility = showFolders ? Visibility.Visible : Visibility.Collapsed;
            UpButton.IsEnabled = folder.Length > 0;
            UpButton.ToolTip = string.Format(Texts.UpFolder, folder.Length == 0 ? Texts.TopFolder : folder.Replace("/", " / "));
        }

        private void FolderToggle_Click(object sender, RoutedEventArgs e) => ShowFoldersChanged?.Invoke(FolderToggle.IsChecked == true);

        private void UpButton_Click(object sender, RoutedEventArgs e) => GoUpRequested?.Invoke();

        private void Bar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.XButton1 || !UpButton.IsVisible || !UpButton.IsEnabled)
                return;

            GoUpRequested?.Invoke();
            e.Handled = true;
        }

        private void PartMenu_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu)
                TextStylePartMenu.Fill(menu, PartMenuItem_Click);
        }

        private void PartMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: TextStyleParts parts } && TextStylePartMenu.FindPreset(sender) is { } preset)
                PartApplyRequested?.Invoke(preset, parts);
        }

        private void OverwriteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TextStylePartMenu.FindPreset(sender) is { } preset)
                OverwriteRequested?.Invoke(preset);
        }

        private void PreviewTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TextStylePartMenu.FindPreset(sender) is { } preset)
                PreviewTextChangeRequested?.Invoke(preset);
        }

        private void DuplicateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TextStylePartMenu.FindPreset(sender) is { } preset)
                DuplicateRequested?.Invoke(preset);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke();

        private void SplitButton_Click(object sender, RoutedEventArgs e) => SplitRequested?.Invoke();

        private void PresetList_Loaded(object sender, RoutedEventArgs e)
        {
            if (PresetList.ItemContainerStyle is not null)
                return;

            PresetList.ItemContainerStyle = new Style(typeof(ListBoxItem), PresetList.TryFindResource(typeof(ListBoxItem)) as Style)
            {
                Setters =
                {
                    new Setter(MarginProperty, new Thickness(0)),
                    new Setter(PaddingProperty, new Thickness(0)),
                    new Setter(BorderThicknessProperty, new Thickness(0)),
                },
            };
        }

        private void PresetList_ScrollChanged(object sender, ScrollChangedEventArgs e) => ReportRealizedPresets();

        private void ScheduleReport() => Dispatcher.BeginInvoke(ReportRealizedPresets, DispatcherPriority.Loaded);

        private void ReportRealizedPresets()
        {
            var presets = IsVisible
                ? FindDescendants<ListBoxItem>(PresetList).Select(item => item.DataContext).OfType<TextStylePreset>().ToList()
                : [];
            RealizedPresetsChanged?.Invoke(presets);
        }

        private object? pressedEntry;

        private Point pressedPoint;

        private void PresetList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            pressedEntry = e.ClickCount == 1 ? FindEntry(e.OriginalSource) : null;
            pressedPoint = e.GetPosition(PresetList);
        }

        private void PresetList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (pressedEntry is not TextStylePreset preset || e.LeftButton != MouseButtonState.Pressed)
                return;

            var delta = e.GetPosition(PresetList) - pressedPoint;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            pressedEntry = null;
            DragDrop.DoDragDrop(PresetList, PresetDragData.ForTextPreset(preset.Current).ToDataObject(), DragDropEffects.Copy);
        }

        private void PresetList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var pressed = pressedEntry;
            pressedEntry = null;
            if (FindEntry(e.OriginalSource) is not TextStylePreset preset || !ReferenceEquals(preset, pressed))
                return;

            PresetList.SelectedItem = null;
            PresetClicked?.Invoke(preset);
        }

        private void PresetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || FindEntry(e.OriginalSource) is not FolderEntry folder)
                return;

            pressedEntry = null;
            PresetList.SelectedItem = null;
            FolderClicked?.Invoke(folder);
            e.Handled = true;
        }

        private void PresetList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.Control && FindEntry(e.OriginalSource) is TextStylePreset preset)
            {
                VariantRequested?.Invoke(preset, e.Delta > 0 ? -1 : 1);
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.None && FindDescendants<ScrollViewer>(PresetList).FirstOrDefault() is { ScrollableWidth: > 0 } viewer)
            {
                viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
                return;
            }

            WheelPassed?.Invoke(e);
        }

        private object? FindEntry(object source)
        {
            for (var element = source as DependencyObject; element is not null && element != PresetList; element = VisualTreeHelper.GetParent(element))
            {
                if (element is ButtonBase)
                    return null;

                if (element is ListBoxItem item)
                    return item.DataContext;
            }

            return null;
        }

        private void FavoriteButton_Click(object sender, RoutedEventArgs e) =>
            Dispatcher.BeginInvoke(PresetLibrary.NotifyFavoriteChanged, DispatcherPriority.Background);

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    yield return match;
                    continue;
                }

                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
