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
using PresetStudio.ViewModel;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace PresetStudio.View
{
    public partial class PresetStudioView : UserControl
    {
        private const double VisiblePrefetchMargin = 300;

        private readonly HashSet<ListBox> hookedLists = [];

        public PresetStudioView()
        {
            InitializeComponent();
            KeepFontToggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PresetStudioSettings.KeepFont)) { Source = PresetStudioSettings.Default, Mode = BindingMode.TwoWay });
            KeepFontSizeToggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PresetStudioSettings.KeepFontSize)) { Source = PresetStudioSettings.Default, Mode = BindingMode.TwoWay });
            ShowInItemEditorToggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PresetStudioSettings.ShowInItemEditor)) { Source = PresetStudioSettings.Default, Mode = BindingMode.TwoWay });

            dragStarter = new DragStarter(this, entry => DataContext is PresetStudioViewModel ? PresetStudioViewModel.CreateDragData(entry) : null);
            dragStarter.DragFinished += () =>
            {
                pressedPreset = null;
                (DataContext as PresetStudioViewModel)?.EndPointerSelection(null);
            };
            AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(PresetPointer_Down), true);
            AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(PresetPointer_Up), true);
        }

        private void TabControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TabControl tabs)
                return;

            var hidden = PresetStudioSettings.Default.HiddenTabs;
            foreach (var tab in tabs.Items.OfType<TabItem>())
                tab.Visibility = hidden.Contains(GetTabKey(tab)) ? Visibility.Collapsed : Visibility.Visible;

            if (tabs.Items.OfType<TabItem>().All(tab => tab.Visibility != Visibility.Visible) && tabs.Items.OfType<TabItem>().FirstOrDefault() is { } first)
                first.Visibility = Visibility.Visible;

            EnsureVisibleSelection(tabs);
        }

        private void TabControl_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TabControl tabs || e.OriginalSource is not DependencyObject source)
                return;

            var element = source;
            while (element is not null && element is not TabItem)
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

            if (element is not TabItem clicked || ItemsControl.ItemsControlFromItemContainer(clicked) != tabs)
                return;

            var menu = new ContextMenu { PlacementTarget = clicked };
            var tabItems = tabs.Items.OfType<TabItem>().ToList();
            foreach (var tab in tabItems)
            {
                var isVisible = tab.Visibility == Visibility.Visible;
                var item = new MenuItem
                {
                    Header = tab.Header,
                    IsCheckable = true,
                    IsChecked = isVisible,
                    IsEnabled = !isVisible || tabItems.Count(other => other.Visibility == Visibility.Visible) > 1,
                };
                item.Click += (_, _) => SetTabVisible(tabs, tab, item.IsChecked);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        private static void SetTabVisible(TabControl tabs, TabItem tab, bool isVisible)
        {
            tab.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            var hidden = PresetStudioSettings.Default.HiddenTabs;
            var key = GetTabKey(tab);
            hidden.Remove(key);
            if (!isVisible)
                hidden.Add(key);

            PresetStudioSettings.Default.Save();
            EnsureVisibleSelection(tabs);
        }

        private static void EnsureVisibleSelection(TabControl tabs)
        {
            if (tabs.SelectedItem is TabItem { Visibility: Visibility.Visible })
                return;

            tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Visibility == Visibility.Visible);
        }

        private static string GetTabKey(TabItem tab) => tab.Tag as string ?? string.Empty;

        private readonly DragStarter dragStarter;
        private TextStylePreset? pressedPreset;

        private void PresetPointer_Down(object sender, MouseButtonEventArgs e)
        {
            pressedPreset = e.ClickCount == 1 ? DragStarter.FindEntry(e.OriginalSource as DependencyObject) as TextStylePreset : null;
            if (pressedPreset is not null && DataContext is PresetStudioViewModel viewModel)
                viewModel.BeginPointerSelection();
        }

        private void PresetPointer_Up(object sender, MouseButtonEventArgs e)
        {
            if (pressedPreset is not { } pressed || dragStarter.IsDragging)
                return;

            pressedPreset = null;
            var released = DragStarter.FindEntry(InputHitTest(e.GetPosition(this)) as DependencyObject);
            var applies = ReferenceEquals(released, pressed) && (Keyboard.Modifiers & ~ModifierKeys.Alt) == ModifierKeys.None;
            (DataContext as PresetStudioViewModel)?.EndPointerSelection(applies ? pressed : null);
        }

        private void ThumbnailList_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBox list)
                return;

            if (hookedLists.Add(list))
            {
                ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) =>
                    Dispatcher.BeginInvoke(() => ReportVisibleItems(list), DispatcherPriority.Loaded);
            }

            ReportVisibleItems(list);
        }

        private void ThumbnailList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ListBox list)
                ReportVisibleItems(list);
        }

        private void ThumbnailList_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ListBox list)
                Dispatcher.BeginInvoke(() => ReportVisibleItems(list), DispatcherPriority.Loaded);
        }

        private void ReportVisibleItems(ListBox list)
        {
            if (DataContext is not PresetStudioViewModel viewModel)
                return;

            var items = new List<IPreviewItem>();
            if (list.IsVisible && list.ActualHeight > 0)
            {
                var viewport = new Rect(0, -VisiblePrefetchMargin, list.ActualWidth, list.ActualHeight + VisiblePrefetchMargin * 2);
                foreach (var container in FindDescendants<ListBoxItem>(list))
                {
                    if (!container.IsVisible || !IsInViewport(container, list, viewport))
                        continue;

                    switch (container.DataContext)
                    {
                        case TextStylePreset preset:
                            items.Add(preset.Current);
                            break;
                        case IPreviewItem item:
                            items.Add(item);
                            break;
                    }
                }
            }

            viewModel.UpdateVisibleItems(list, items);
        }

        private static bool IsInViewport(FrameworkElement element, Visual ancestor, Rect viewport)
        {
            try
            {
                return viewport.IntersectsWith(element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize)));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
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

        private void TemplateItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item || DataContext is not PresetStudioViewModel viewModel)
                return;

            if (viewModel.AddTemplateCommand.CanExecute(item.DataContext))
                viewModel.AddTemplateCommand.Execute(item.DataContext);
        }

        private void ThumbnailList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || DataContext is not PresetStudioViewModel viewModel)
                return;

            for (var element = e.OriginalSource as DependencyObject; element is not null && element != sender; element = VisualTreeHelper.GetParent(element))
            {
                if (element is ListBoxItem { DataContext: TextStylePreset })
                    return;
            }

            viewModel.ChangeThumbnailScale(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private void PresetTab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.XButton1 || DataContext is not PresetStudioViewModel viewModel)
                return;

            if (viewModel.GoUpFolderCommand.CanExecute(null))
                viewModel.GoUpFolderCommand.Execute(null);

            e.Handled = true;
        }

        private void FolderItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item || DataContext is not PresetStudioViewModel viewModel)
                return;

            if (viewModel.OpenFolderCommand.CanExecute(item.DataContext))
                viewModel.OpenFolderCommand.Execute(item.DataContext);
        }

        private void EffectItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item || DataContext is not PresetStudioViewModel viewModel)
                return;

            if (viewModel.AddEffectCommand.CanExecute(item.DataContext))
                viewModel.AddEffectCommand.Execute(item.DataContext);
        }

        private void PreviewItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is ListBoxItem { DataContext: IPreviewItem target } && DataContext is PresetStudioViewModel viewModel)
                viewModel.StartPreviewAnimation(target);
        }

        private void PreviewItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is PresetStudioViewModel viewModel)
                viewModel.StopPreviewAnimation();
        }

        private void PresetItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is FrameworkElement { ContextMenu: { } menu } && menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Tag is "PartMenu") is { } partMenu)
                TextStylePartMenu.Fill(partMenu, PartMenuItem_Click);
        }

        private void PartMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: TextStyleParts parts } && TextStylePartMenu.FindPreset(sender) is { } preset && DataContext is PresetStudioViewModel viewModel)
                viewModel.ApplyPresetParts(preset, parts);
        }

        private void MenuButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button { ContextMenu: { } menu } button)
                return;

            e.Handled = true;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.RuleImages.SetSelected(list.SelectedItems.Cast<object>());
        }

        private void FontTab_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && DataContext is PresetStudioViewModel viewModel)
                viewModel.Fonts.Refresh();
        }

        private void FontItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            for (var element = e.OriginalSource as DependencyObject; element is not null && element != sender; element = VisualTreeHelper.GetParent(element))
            {
                if (element is System.Windows.Controls.Primitives.ButtonBase)
                    return;
            }

            if (Keyboard.Modifiers == ModifierKeys.None && sender is ListBoxItem { DataContext: FontEntry entry } && DataContext is PresetStudioViewModel viewModel)
                viewModel.Fonts.Apply(entry);
        }

        private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.Fonts.SetSelected(list.SelectedItems.Cast<object>());
        }

        private void FontFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PresetStudioViewModel viewModel)
                Dispatcher.BeginInvoke(viewModel.Fonts.SaveFavorites, DispatcherPriority.Background);
        }

        private void TachieTab_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && DataContext is PresetStudioViewModel viewModel)
            {
                viewModel.Tachie.RefreshCharacters();
                viewModel.Tachie.RebuildEntries();
            }
        }

        private void FaceEditor_PropertyEditorAttributeCreated(object? sender, PropertyEditorAttributeCreatedArgs e)
        {
            if (e.PropertyEditorAttribute is IPropertyEditorForTachieParameterAttribute attribute && DataContext is PresetStudioViewModel { Tachie.SelectedCharacter: { } character })
                attribute.CharacterParameter = character.TachieCharacterParameter;
        }

        private void FaceEditor_EndEdit(object? sender, EventArgs e)
        {
            if (DataContext is PresetStudioViewModel viewModel)
                viewModel.Tachie.ApplyEditingToSelection();
        }

        private void FacePartItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem { DataContext: FacePartOption option } item && DataContext is PresetStudioViewModel viewModel)
            {
                viewModel.Tachie.SelectPart(option);
                item.IsSelected = false;
            }
        }

        private void TachieList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.Tachie.SetSelected(list.SelectedItems.Cast<object>());
        }

        private void TachieItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || sender is not ListBoxItem { DataContext: IPreviewItem entry } || DataContext is not PresetStudioViewModel viewModel)
                return;

            if (viewModel.Tachie.ApplyCommand.CanExecute(entry))
                viewModel.Tachie.ApplyCommand.Execute(entry);
        }

        private void RuleTab_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && DataContext is PresetStudioViewModel viewModel)
                viewModel.RuleImages.Refresh();
        }

        private void RuleItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (DataContext is not PresetStudioViewModel viewModel || sender is not ListBoxItem item)
                return;

            switch (item.DataContext)
            {
                case RuleImagePreset rule:
                    viewModel.StopPreviewAnimation();
                    viewModel.RuleImages.StartAnimation(rule);
                    break;
                case TransitionPreset transition:
                    viewModel.RuleImages.StopAnimation();
                    viewModel.StartPreviewAnimation(transition);
                    break;
            }
        }

        private void RuleItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is not PresetStudioViewModel viewModel)
                return;

            viewModel.RuleImages.StopAnimation();
            viewModel.StopPreviewAnimation();
        }

        private void RuleItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem { DataContext: ITransitionEntry entry } && DataContext is PresetStudioViewModel viewModel && viewModel.RuleImages.ApplyCommand.CanExecute(entry))
                viewModel.RuleImages.ApplyCommand.Execute(entry);
        }

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.SetSelectedEntries(list.SelectedItems.Cast<object>());
        }

        private void AudioEffectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.AudioEffects.SetSelected(list.SelectedItems.Cast<object>());
        }

        private void AudioEffectItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AudioEffectPreset preset } && DataContext is PresetStudioViewModel viewModel)
                viewModel.AudioEffects.RequestWaveform(preset);
        }

        private void AudioEffectItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem { DataContext: AudioEffectPreset preset } || DataContext is not PresetStudioViewModel viewModel)
                return;

            if (e.ClickCount == 2)
            {
                if (viewModel.AudioEffects.AddEffectCommand.CanExecute(preset))
                    viewModel.AudioEffects.AddEffectCommand.Execute(preset);
                return;
            }

            if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None && viewModel.AudioEffects.PlayCommand.CanExecute(preset))
                viewModel.AudioEffects.PlayCommand.Execute(preset);
        }

        private void EffectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.SetSelectedEffects(list.SelectedItems.Cast<object>());
        }

        private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox list && DataContext is PresetStudioViewModel viewModel)
                viewModel.SetSelectedTemplates(list.SelectedItems.Cast<object>());
        }

        private void ListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private void PresetItem_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            if (sender is not ListBoxItem { DataContext: TextStylePreset preset } || DataContext is not PresetStudioViewModel viewModel)
                return;

            viewModel.MoveVariant(preset, e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        }
    }
}
