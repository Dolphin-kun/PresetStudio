using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PresetStudio.Commons;
using PresetStudio.Model;
using PresetStudio.Preview;
using PresetStudio.Settings;
using PresetStudio.ViewModel;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;
using YukkuriMovieMaker.Views;

namespace PresetStudio.View
{
    internal static class ItemEditorPresetHook
    {
        private static bool isRegistered;

        public static void Register()
        {
            if (isRegistered)
                return;
            isRegistered = true;

            EventManager.RegisterClassHandler(typeof(ItemEditorView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemEditorView view || ItemEditorPresetSession.IsAttached(view))
                return;

            try
            {
                ItemEditorPresetSession.Attach(view);
            }
            catch
            {
            }
        }
    }

    internal sealed class ItemEditorPresetSession
    {
        private static readonly DependencyProperty SessionProperty =
            DependencyProperty.RegisterAttached("Session", typeof(ItemEditorPresetSession), typeof(ItemEditorPresetSession));

        private static readonly NaturalComparer nameComparer = new();
        private static readonly Comparer<string> folderComparer = Comparer<string>.Create(CompareFolders);
        private static readonly PreviewVideoInfo previewVideoInfo = new(1920, 1080, 30, 48000);

        private readonly ItemEditorView view;
        private readonly ItemEditorPresetBar bar = new();
        private readonly ObservableCollection<object> entries = [];
        private readonly List<TextStylePreset> watchedPresets = [];
        private string shownFolder = string.Empty;
        private readonly HashSet<TextStylePreset> pendingThumbnails = new(ReferenceEqualityComparer.Instance);
        private volatile HashSet<TextStylePreset> wantedThumbnails = new(ReferenceEqualityComparer.Instance);
        private PropertiesEditor? editor;
        private ItemsControl? groupsControl;
        private FrameworkElement? container;
        private Thickness containerMargin;
        private AdornerLayer? layer;
        private PresetBarAdorner? adorner;
        private RenderWorker? worker;
        private Window? ownerWindow;
        private (int Start, int Length)? pressedRange;
        private object? observedItems;
        private object? cachedGroupItems;
        private object? cachedGroups;
        private object? cachedGroup;
        private FrameworkElement? cachedGroupContainer;
        private Expander? cachedExpander;

        private ItemEditorPresetSession(ItemEditorView view)
        {
            this.view = view;
        }

        public static bool IsAttached(ItemEditorView view) => view.GetValue(SessionProperty) is not null;

        public static void Attach(ItemEditorView view)
        {
            var session = new ItemEditorPresetSession(view);
            view.SetValue(SessionProperty, session);
            session.Start();
        }

        private static PresetStudioSettings Settings => PresetStudioSettings.Default;

        private static ObservableCollection<TextStylePreset> Source => Settings.Presets;

        private void Start()
        {
            bar.ItemsSource = entries;
            ThumbnailCache.Register(this, item => item is TextStylePreset preset && wantedThumbnails.Contains(preset));
            bar.AddRequested += AddPreset;
            bar.SplitRequested += () => _ = SplitTextAsync();
            bar.WheelPassed += PassWheel;
            bar.PresetClicked += preset => Apply(preset, TextStyleApplier.CurrentParts, TakePressedRange());
            bar.PartApplyRequested += (preset, parts) => Apply(preset, parts, TakePressedRange());
            bar.OverwriteRequested += OverwritePreset;
            bar.DuplicateRequested += DuplicatePreset;
            bar.PreviewTextChangeRequested += ChangePreviewText;
            bar.VariantRequested += MoveVariant;
            bar.RealizedPresetsChanged += RenderThumbnails;
            bar.ShowFoldersChanged += showFolders => Settings.ItemEditorShowFolders = showFolders;
            bar.GoUpRequested += () => Settings.ItemEditorFolder = PresetLibrary.GetParentFolder(shownFolder);
            bar.FolderClicked += folder => Settings.ItemEditorFolder = folder.Path;
            Source.CollectionChanged += Source_CollectionChanged;
            Settings.PresetFolders.CollectionChanged += Source_CollectionChanged;
            Settings.PropertyChanged += Settings_PropertyChanged;
            PresetLibrary.FavoriteChanged += PresetLibrary_FavoriteChanged;
            PresetLinkStore.LinksChanged += RefreshLinkHighlight;
            RebuildPresets();

            view.Unloaded += View_Unloaded;
            view.LayoutUpdated += View_LayoutUpdated;
            ownerWindow = Window.GetWindow(view);
            ownerWindow?.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);
            Update();
        }

        private void View_Unloaded(object sender, RoutedEventArgs e)
        {
            view.Unloaded -= View_Unloaded;
            view.LayoutUpdated -= View_LayoutUpdated;
            ownerWindow?.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown));
            ownerWindow = null;
            Source.CollectionChanged -= Source_CollectionChanged;
            Settings.PresetFolders.CollectionChanged -= Source_CollectionChanged;
            Settings.PropertyChanged -= Settings_PropertyChanged;
            PresetLibrary.FavoriteChanged -= PresetLibrary_FavoriteChanged;
            PresetLinkStore.LinksChanged -= RefreshLinkHighlight;
            ThumbnailCache.Unregister(this);
            UnwatchPresets();

            try
            {
                Detach();
            }
            catch
            {
            }

            worker?.Dispose();
            worker = null;
            view.ClearValue(SessionProperty);
        }

        private void View_LayoutUpdated(object? sender, EventArgs e) => Update();

        private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPresets();

        private void PresetLibrary_FavoriteChanged(object? sender, EventArgs e) => RebuildPresets();

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PresetStudioSettings.PreviewText))
            {
                foreach (var preset in wantedThumbnails)
                    RenderThumbnail(preset);
                return;
            }

            if (e.PropertyName is nameof(PresetStudioSettings.ItemEditorFolder) or nameof(PresetStudioSettings.ItemEditorShowFolders))
                RebuildPresets();
        }

        private void Preset_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TextStylePreset.Folder))
                RebuildPresets();
        }

        private void UnwatchPresets()
        {
            foreach (var preset in watchedPresets)
                preset.PropertyChanged -= Preset_PropertyChanged;
            watchedPresets.Clear();
        }

        private void RebuildPresets()
        {
            UnwatchPresets();
            foreach (var preset in Source)
            {
                preset.PropertyChanged += Preset_PropertyChanged;
                watchedPresets.Add(preset);
            }

            var showFolders = Settings.ItemEditorShowFolders;
            var folders = PresetLibrary.GetAllFolders();
            var folder = showFolders && folders.Contains(Settings.ItemEditorFolder) ? Settings.ItemEditorFolder : string.Empty;
            shownFolder = folder;
            bar.SetFolderState(showFolders, folder);

            entries.Clear();
            if (!showFolders)
            {
                foreach (var preset in Source.OrderByDescending(preset => preset.IsFavorite).ThenBy(preset => preset.Folder, folderComparer))
                    entries.Add(preset);
                return;
            }

            foreach (var path in folders.Where(path => PresetLibrary.GetParentFolder(path) == folder).Order(folderComparer))
                entries.Add(new FolderEntry(PresetLibrary.GetFolderName(path), path));

            foreach (var preset in Source.Where(preset => preset.Folder == folder).OrderByDescending(preset => preset.IsFavorite))
                entries.Add(preset);
        }

        private void PassWheel(MouseWheelEventArgs e)
        {
            DependencyObject? element = container;
            while (element is not null && element is not ScrollViewer)
                element = VisualTreeHelper.GetParent(element);

            if (element is ScrollViewer viewer)
            {
                viewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = viewer,
                });
            }
        }

        private void RefreshLinkHighlight()
        {
            var ids = view.DataContext is ItemEditorViewModel itemEditor
                ? itemEditor.Items.Value.Select(PresetLinkStore.GetLink).OfType<PresetLink>().Select(link => link.PresetId).ToHashSet()
                : [];
            foreach (var preset in Source)
                preset.IsLinked = ids.Count > 0 && preset.Variants.Prepend(preset).Any(variant => ids.Contains(variant.Id));
        }

        private static int CompareFolders(string? x, string? y)
        {
            var left = (x ?? string.Empty).Split('/');
            var right = (y ?? string.Empty).Split('/');
            for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                var result = nameComparer.Compare(left[i], right[i]);
                if (result != 0)
                    return result;
            }

            return left.Length.CompareTo(right.Length);
        }

        private void Update()
        {
            if (view.DataContext is ItemEditorViewModel itemEditor && !ReferenceEquals(itemEditor.Items.Value, observedItems))
            {
                observedItems = itemEditor.Items.Value;
                if (itemEditor.Items.Value.Count > 0)
                    PresetLinkStore.EnsureScope(TimelineLocator.FindScenes(view));
                RefreshLinkHighlight();
            }

            var target = FindTextGroupContent();
            if (target != container)
            {
                Detach();
                if (target is not null)
                    AttachTo(target);
            }

            if (container is null)
                return;

            var margin = new Thickness(containerMargin.Left, containerMargin.Top + bar.DesiredSize.Height, containerMargin.Right, containerMargin.Bottom);
            if (container.Margin != margin)
                container.Margin = margin;
        }

        private FrameworkElement? FindTextGroupContent()
        {
            if (!view.IsVisible || view.DataContext is not ItemEditorViewModel viewModel)
                return null;

            var items = viewModel.Items.Value;
            if (items.Count == 0)
                return null;

            editor ??= FindDescendants<PropertiesEditor>(view).FirstOrDefault();
            if (editor is null)
                return null;

            var groups = editor.Groups;
            if (!ReferenceEquals(cachedGroupItems, items) || !ReferenceEquals(cachedGroups, groups))
            {
                cachedGroupItems = items;
                cachedGroups = groups;
                cachedGroup = items.All(item => item is TextItem)
                    ? groups.FirstOrDefault(group => group.Items.Any(pair =>
                        pair.EditableProperty is { PropertyOwner: TextItem, Property.Name: nameof(TextItem.Text) }))
                    : null;
            }

            if (cachedGroup is null)
            {
                cachedGroupItems = null;
                return null;
            }

            groupsControl ??= FindDescendants<ItemsControl>(editor).FirstOrDefault(control => control.ItemsSource == editor.Groups);
            if (groupsControl?.ItemContainerGenerator.ContainerFromItem(cachedGroup) is not FrameworkElement groupContainer)
                return null;

            if (!ReferenceEquals(cachedGroupContainer, groupContainer) || cachedExpander is null)
            {
                cachedGroupContainer = groupContainer;
                cachedExpander = FindDescendants<Expander>(groupContainer).FirstOrDefault();
            }

            return cachedExpander is { IsExpanded: true, Content: FrameworkElement content } && content.IsVisible
                ? content
                : null;
        }

        private void AttachTo(FrameworkElement target)
        {
            if (AdornerLayer.GetAdornerLayer(target) is not { } targetLayer)
                return;

            container = target;
            containerMargin = target.Margin;
            layer = targetLayer;
            adorner = new PresetBarAdorner(target, bar);
            layer.Add(adorner);
        }

        private void Detach()
        {
            container?.Margin = containerMargin;

            if (adorner is not null)
            {
                layer?.Remove(adorner);
                adorner.Release();
            }

            container = null;
            layer = null;
            adorner = null;
        }

        private void AddPreset()
        {
            if (view.DataContext is not ItemEditorViewModel viewModel)
                return;

            var items = viewModel.Items.Value.OfType<TextItem>().ToList();
            if (items.Count == 0)
                return;

            var owner = Window.GetWindow(view);
            var title = items.Count == 1 ? Texts.AddPreset : string.Format(Texts.AddPresets, items.Count);
            if (NameInputViewModel.Show(title, Texts.NameLabel, PresetLibrary.GetNames(), PresetLibrary.GetCategories(), owner: owner, previewText: string.Empty) is not { } input)
                return;

            var placed = PresetLibrary.PutItems(input.Name, input.Category, shownFolder, items, input.PreviewText, owner);
            if (placed.Count == 0)
                return;

            foreach (var (group, target) in placed)
                group.ShowVariant(target);
            PresetStudioSettings.Default.Save();

            var targets = placed.Select(x => x.Target).ToList();
            wantedThumbnails = new HashSet<TextStylePreset>(wantedThumbnails.Concat(targets), ReferenceEqualityComparer.Instance);
            foreach (var target in targets)
                RenderThumbnail(target);
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Visual source && (source == bar || bar.IsAncestorOf(source)))
                pressedRange = GetTextSelection();
        }

        private (int Start, int Length)? TakePressedRange()
        {
            var range = pressedRange;
            pressedRange = null;
            return range;
        }

        private (int Start, int Length)? GetTextSelection()
        {
            if (container is null || FindDescendants<RichTextEditor>(container).FirstOrDefault() is not { IsKeyboardFocusWithin: true } textEditor)
                return null;

            if (textEditor.Template?.FindName("PARTS_RichTextBox", textEditor) is RichTextBox { IsVisible: true } richTextBox)
            {
                var start = new TextRange(richTextBox.Document.ContentStart, richTextBox.Selection.Start).Text.Length;
                var end = new TextRange(richTextBox.Document.ContentStart, richTextBox.Selection.End).Text.Length;
                return end > start ? (start, end - start) : null;
            }

            if (textEditor.Template?.FindName("PARTS_TextBox", textEditor) is TextBox { IsVisible: true, SelectionLength: > 0 } textBox)
                return (textBox.SelectionStart, textBox.SelectionLength);

            return null;
        }

        private void OverwritePreset(TextStylePreset preset)
        {
            if (view.DataContext is not ItemEditorViewModel viewModel || viewModel.Items.Value.OfType<TextItem>().FirstOrDefault() is not { } item)
                return;

            var target = preset.Current;
            if (!PresetLibrary.Confirm(string.Format(Texts.ConfirmOverwriteWithSelected, target.Name), Window.GetWindow(view)))
                return;

            var oldStyle = (TextItem)target.Item.GetClone();
            var linked = PresetLinkStore.FindLinked(TimelineLocator.FindAllTimelines(view), target).Where(linkedItem => linkedItem != item).ToList();

            PresetLibrary.Overwrite(target, item);
            wantedThumbnails = new HashSet<TextStylePreset>(wantedThumbnails.Append(target), ReferenceEqualityComparer.Instance);
            RenderThumbnail(target);
            PresetLinkStore.Link(item, target, TextStyleParts.All);

            if (linked.Count > 0 && PresetLibrary.Confirm(string.Format(Texts.ConfirmUpdateLinked, target.Name, linked.Count), Window.GetWindow(view)))
            {
                viewModel.BeginEditCommand.Execute();
                foreach (var linkedItem in linked)
                    TextStyleApplier.ReplaceOnItem(linkedItem, oldStyle, target.Item, PresetLinkStore.GetLink(linkedItem)?.Parts ?? TextStyleApplier.DefaultParts);
                viewModel.EndEditCommand.Execute();
            }
        }

        private void ChangePreviewText(TextStylePreset preset)
        {
            var target = preset.Current;
            var placeHolder = string.Format(Texts.PreviewTextEmptyDefault, Settings.PreviewText);
            if (NameInputViewModel.Show(Texts.ChangePreviewText, placeHolder, [], initialName: target.PreviewText ?? string.Empty, allowEmpty: true, owner: Window.GetWindow(view)) is not { Name: var text })
                return;

            target.PreviewText = text;
            target.PreviewImage = null;
            Settings.Save();
            wantedThumbnails = new HashSet<TextStylePreset>(wantedThumbnails.Append(target), ReferenceEqualityComparer.Instance);
            RenderThumbnail(target);
        }

        private void DuplicatePreset(TextStylePreset preset)
        {
            var (Group, Target) = PresetLibrary.Duplicate(preset);
            Group.ShowVariant(Target);
            Settings.Save();
        }

        private void Apply(TextStylePreset preset, TextStyleParts parts, (int Start, int Length)? range)
        {
            if (view.DataContext is not ItemEditorViewModel viewModel || preset.Current is not { } source)
                return;

            var targets = viewModel.Items.Value.OfType<TextItem>().ToList();
            if (targets.Count == 0)
                return;

            if (range is { } selection && targets.Count == 1 && TextSplitPlan.CanSplit(targets[0], selection.Start, selection.Length))
            {
                _ = ApplyToRangeAsync(viewModel, targets[0], source, selection, parts);
                return;
            }

            viewModel.BeginEditCommand.Execute();
            foreach (var target in targets)
            {
                PresetLinkStore.ApplyPreset(target, source, parts);
            }
            viewModel.EndEditCommand.Execute();
        }

        private async Task SplitTextAsync()
        {
            var range = TakePressedRange();
            var owner = Window.GetWindow(view);
            if (view.DataContext is not ItemEditorViewModel viewModel)
                return;

            if (viewModel.Items.Value is not [TextItem target])
            {
                MessageWindow.Inform(Texts.SelectOneTextItemToSplit, owner);
                return;
            }

            if (TextSplitViewModel.Show(target, range, owner) is not { } request)
                return;

            if (TimelineLocator.Find(view) is not { } timeline || !timeline.Items.Contains(target))
            {
                MessageWindow.Inform(Texts.SplitNoTimeline, owner);
                return;
            }

            void begin() => viewModel.BeginEditCommand.Execute();
            void end() => viewModel.EndEditCommand.Execute();
            var result = await TextSplitService.SplitSegmentsAsync(timeline, target, request.Mode, request.Boundaries, request.Interval, begin, end);
            if (TextSplitService.GetMessage(result) is { } message)
                MessageWindow.Inform(message, owner);
        }

        private async Task ApplyToRangeAsync(ItemEditorViewModel viewModel, TextItem target, TextStylePreset source, (int Start, int Length) range, TextStyleParts parts)
        {
            var owner = Window.GetWindow(view);
            if (TimelineLocator.Find(view) is not { } timeline || !timeline.Items.Contains(target))
            {
                MessageWindow.Inform(Texts.ApplyRangeNoTimeline, owner);
                return;
            }

            var result = await TextSplitService.SplitAsync(
                timeline,
                target,
                source,
                range.Start,
                range.Length,
                parts,
                () => viewModel.BeginEditCommand.Execute(),
                () => viewModel.EndEditCommand.Execute());
            if (TextSplitService.GetMessage(result) is { } message)
                MessageWindow.Inform(message, owner);
        }

        private void MoveVariant(TextStylePreset preset, int delta)
        {
            if (preset.Variants.Count == 0)
                return;

            preset.MoveVariant(delta);
            Apply(preset, TextStyleApplier.CurrentParts, null);
            wantedThumbnails = new HashSet<TextStylePreset>(wantedThumbnails.Append(preset.Current), ReferenceEqualityComparer.Instance);
            RenderThumbnail(preset.Current);
        }

        private void RenderThumbnails(IReadOnlyList<TextStylePreset> realized)
        {
            wantedThumbnails = new HashSet<TextStylePreset>(realized.Select(preset => preset.Current), ReferenceEqualityComparer.Instance);
            foreach (var preset in realized)
            {
                ThumbnailCache.Touch(preset.Current);
                RenderThumbnail(preset.Current);
            }
        }

        private void RenderThumbnail(TextStylePreset preset)
        {
            if (preset.PreviewImage is not null || pendingThumbnails.Contains(preset))
                return;

            try
            {
                worker ??= new RenderWorker(previewVideoInfo, "PresetStudio item editor thumbnail");
            }
            catch
            {
                return;
            }

            pendingThumbnails.Add(preset);
            _ = RenderThumbnailAsync(worker, preset);
        }

        private async Task RenderThumbnailAsync(RenderWorker targetWorker, TextStylePreset preset)
        {
            try
            {
                var text = PresetLibrary.GetPreviewText(preset);
                var image = await targetWorker.Enqueue(renderer =>
                    preset.PreviewImage is null && wantedThumbnails.Contains(preset)
                        ? renderer.Render(PresetPreviewRenderer.CreatePreviewItems([preset.Item], text), null)
                        : null);

                if (image is not null && preset.PreviewImage is null && text == PresetLibrary.GetPreviewText(preset))
                {
                    preset.PreviewImage = image;
                    ThumbnailCache.Add(preset);
                }
            }
            catch
            {
            }
            finally
            {
                pendingThumbnails.Remove(preset);
            }
        }

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
