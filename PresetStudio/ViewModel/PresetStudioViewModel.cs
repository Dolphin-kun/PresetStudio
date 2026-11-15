using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PresetStudio.Model;
using PresetStudio.Preview;
using PresetStudio.Settings;
using PresetStudio.View;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Resources.Localization;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace PresetStudio.ViewModel
{
    internal sealed class PresetStudioViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        private const double MinThumbnailScale = 0.5;
        private const double MaxThumbnailScale = 2;
        private const double ThumbnailScaleStep = 0.1;

        private static readonly NaturalComparer nameComparer = new();

        private Timeline? timeline;
        private UndoRedoManager? undoRedoManager;
        private Scenes? scenes;
        private RenderWorker? thumbnailWorker;
        private RenderWorker? animationWorker;
        private PreviewAnimation? previewAnimation;
        private IReadOnlyList<IItem> selectedItems = [];
        private readonly Dictionary<object, IReadOnlyList<IPreviewItem>> visibleItemsBySource = [];
        private readonly HashSet<IPreviewItem> pendingThumbnails = new(ReferenceEqualityComparer.Instance);
        private volatile HashSet<IPreviewItem> wantedThumbnails = new(ReferenceEqualityComparer.Instance);
        private List<EffectPreset>? defaultEffects;
        private bool isAddingTemplate;
        private bool isRebuildingEntries;

        public static ObservableCollection<TextStylePreset> Presets => PresetStudioSettings.Default.Presets;

        public ObservableCollection<TemplatePreset> Templates { get; } = [];

        public ObservableCollection<EffectPreset> Effects { get; } = [];

        public ObservableCollection<object> PresetEntries { get; } = [];

        public RuleImageTabViewModel RuleImages { get; }

        public TachieTabViewModel Tachie { get; }

        public FontTabViewModel Fonts { get; }

        public AudioEffectTabViewModel AudioEffects { get; }

        private object? selectedEntry;
        public object? SelectedEntry
        {
            get => selectedEntry;
            set
            {
                if (!Set(ref selectedEntry, value) || isRebuildingEntries)
                    return;

                SelectedPreset = value as TextStylePreset;
            }
        }

        private string currentFolder = string.Empty;
        public string CurrentFolder { get => currentFolder; private set => Set(ref currentFolder, value, nameof(CurrentFolder), nameof(UpFolderToolTip)); }

        public double ThumbnailWidth => (IsPreviewOnly ? 100 : 160) * PresetStudioSettings.Default.ThumbnailScale;

        public double ThumbnailHeight => (IsPreviewOnly ? 100 : 90) * PresetStudioSettings.Default.ThumbnailScale;

        public double FolderIconSize => (IsPreviewOnly ? 64 : 80) * PresetStudioSettings.Default.ThumbnailScale;

        public bool IsPreviewOnly
        {
            get => PresetStudioSettings.Default.IsPreviewOnly;
            set
            {
                if (PresetStudioSettings.Default.IsPreviewOnly == value)
                    return;

                PresetStudioSettings.Default.IsPreviewOnly = value;
                OnPropertyChanged(nameof(IsPreviewOnly));
                OnPropertyChanged(nameof(ShowsLabels));
                OnPropertyChanged(nameof(ThumbnailWidth));
                OnPropertyChanged(nameof(ThumbnailHeight));
                OnPropertyChanged(nameof(FolderIconSize));
            }
        }

        public bool ShowsLabels => !IsPreviewOnly;

        public string UpFolderToolTip => string.Format(Texts.UpFolder, currentFolder.Length == 0 ? Texts.TopFolder : currentFolder.Replace("/", " / "));

        private string presetSearchText = string.Empty;
        public string PresetSearchText
        {
            get => presetSearchText;
            set
            {
                if (Set(ref presetSearchText, value ?? string.Empty))
                    RebuildPresetEntries();
            }
        }

        private string effectSearchText = string.Empty;
        public string EffectSearchText
        {
            get => effectSearchText;
            set
            {
                if (Set(ref effectSearchText, value ?? string.Empty))
                    CollectionViewSource.GetDefaultView(Effects).Refresh();
            }
        }

        private string templateSearchText = string.Empty;
        public string TemplateSearchText
        {
            get => templateSearchText;
            set
            {
                if (Set(ref templateSearchText, value ?? string.Empty))
                    CollectionViewSource.GetDefaultView(Templates).Refresh();
            }
        }

        private bool isPointerSelecting;

        public void BeginPointerSelection() => isPointerSelecting = true;

        public void EndPointerSelection(TextStylePreset? clicked)
        {
            isPointerSelecting = false;
            if (clicked is not null)
                ApplyPreset(clicked);
        }

        public static PresetDragData? CreateDragData(object entry) => entry switch
        {
            TextStylePreset preset => PresetDragData.ForTextPreset(preset.Current),
            FontEntry font => new PresetDragData
            {
                CanDrop = item => item is TextItem or VoiceItem,
                DropAsync = target => Task.FromResult(DropFont(font, target.Item)),
            },
            AudioEffectPreset audioEffect => new PresetDragData
            {
                CanDrop = AudioEffectTabViewModel.CanDrop,
                DropAsync = target => Task.FromResult(AudioEffectTabViewModel.AddTo(audioEffect, target.Item)),
            },
            EffectPreset effect => new PresetDragData
            {
                CanDrop = item => item is IVideoItem,
                DropAsync = target => Task.FromResult(DropEffect(effect, target.Item)),
            },
            ITransitionEntry transition => new PresetDragData
            {
                CanDrop = item => item is not null && RuleImageTabViewModel.CanDrop(transition, item),
                DropAsync = target => Task.FromResult(RuleImageTabViewModel.Drop(transition, target.Timeline, target.Item, target.Frame, target.Layer)),
            },
            FacePreset or TachieItemPreset => new PresetDragData
            {
                CanDrop = item => item is not null && TachieTabViewModel.CanDrop((IPreviewItem)entry, item),
                DropAsync = target => Task.FromResult(TachieTabViewModel.Drop((IPreviewItem)entry, target.Timeline, target.Item, target.Frame, target.Layer)),
            },
            _ => null,
        };

        private static bool DropFont(FontEntry font, IItem? target)
        {
            switch (target)
            {
                case TextItem text:
                    text.Font = font.Name;
                    return true;
                case VoiceItem voice:
                    voice.Font = font.Name;
                    return true;
                default:
                    return false;
            }
        }

        private static bool DropEffect(EffectPreset effect, IItem? target)
        {
            if (target is not IVideoItem item)
                return false;

            item.VideoEffects = item.VideoEffects.AddRange(effect.Template.CreateEffects());
            return true;
        }

        private TextStylePreset? selectedPreset;
        public TextStylePreset? SelectedPreset
        {
            get => selectedPreset;
            set
            {
                if (!Set(ref selectedPreset, value))
                    return;

                if (!isPointerSelecting)
                    ApplyPreset(value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private IReadOnlyList<object> selectedEntries = [];
        private IReadOnlyList<EffectPreset> selectedEffects = [];
        private IReadOnlyList<TemplatePreset> selectedTemplates = [];

        private TemplatePreset? selectedTemplate;
        public TemplatePreset? SelectedTemplate { get => selectedTemplate; set => Set(ref selectedTemplate, value); }

        private EffectPreset? selectedEffect;
        public EffectPreset? SelectedEffect
        {
            get => selectedEffect;
            set
            {
                if (Set(ref selectedEffect, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public TextItem? SelectedTextItem => selectedItems.OfType<TextItem>().FirstOrDefault();

        public bool HasSelectedTextItem => SelectedTextItem is not null;


        public IVideoItem? SelectedVideoItem => selectedItems.OfType<IVideoItem>().FirstOrDefault();

        public ActionCommand AddPresetCommand { get; }

        public ActionCommand DeletePresetCommand { get; }

        public ActionCommand EditPresetCommand { get; }

        public ActionCommand EditCategoryCommand { get; }

        public ActionCommand MoveVariantForwardCommand { get; }

        public ActionCommand MoveVariantBackwardCommand { get; }

        public ActionCommand EditTagsCommand { get; }

        public ActionCommand MovePresetCommand { get; }

        public ActionCommand NewFolderCommand { get; }

        public ActionCommand OpenFolderCommand { get; }

        public ActionCommand GoUpFolderCommand { get; }

        public ActionCommand RenameFolderCommand { get; }

        public ActionCommand DeleteFolderCommand { get; }

        public ActionCommand AddTemplateCommand { get; }

        public ActionCommand RestoreTemplatesCommand { get; }

        public ActionCommand RegisterTemplateAsPresetCommand { get; }

        public ActionCommand ToggleTemplateCanvasCommand { get; }

        public ActionCommand OverwritePresetCommand { get; }

        public ActionCommand EditTemplateFieldsCommand { get; }


        public ActionCommand MergePresetsCommand { get; }

        public ActionCommand ChangePreviewTextCommand { get; }

        public ActionCommand ChangePresetPreviewTextCommand { get; }

        public ActionCommand DuplicatePresetCommand { get; }

        public ActionCommand AddEffectCommand { get; }

        public ActionCommand AddEffectTemplateCommand { get; }

        public ActionCommand DeleteEffectTemplateCommand { get; }

        public ActionCommand EditEffectTemplateCommand { get; }

        public ActionCommand SelectEffectPreviewImageCommand { get; }

        public ActionCommand ResetEffectPreviewImageCommand { get; }

        private string? templateWarning;
        public string? TemplateWarning { get => templateWarning; private set => Set(ref templateWarning, value, nameof(TemplateWarning), nameof(HasTemplateWarning)); }

        public bool HasTemplateWarning => TemplateWarning is not null;

        public PresetStudioViewModel()
        {
            ThumbnailCache.Register(this, item => wantedThumbnails.Contains(item));
            RuleImages = new RuleImageTabViewModel(() => timeline, () => selectedItems, () => undoRedoManager?.Record());
            RuleImages.SampleImagesChanged += RuleImages_SampleImagesChanged;
            Tachie = new TachieTabViewModel(() => timeline, () => selectedItems, () => undoRedoManager?.Record());
            Fonts = new FontTabViewModel(() => selectedItems, () => undoRedoManager?.Record());
            AudioEffects = new AudioEffectTabViewModel(() => selectedItems, () => undoRedoManager?.Record());

            AddPresetCommand = new ActionCommand(
                _ => SelectedTextItem is not null,
                _ => AddPreset());

            DeletePresetCommand = new ActionCommand(
                parameter => GetTargetEntries(parameter).Count != 0,
                parameter => DeleteEntries(GetTargetEntries(parameter)));

            EditPresetCommand = new ActionCommand(
                parameter => GetTargetPreset(parameter) is not null,
                parameter => EditPreset(GetTargetPreset(parameter)));

            EditCategoryCommand = new ActionCommand(
                parameter => GetTargetPreset(parameter) is { } preset && PresetLibrary.GetCategory(preset).Length > 0,
                parameter => EditCategory(GetTargetPreset(parameter)));

            MoveVariantForwardCommand = new ActionCommand(
                parameter => GetTargetPreset(parameter)?.CanMoveCurrent(-1) == true,
                parameter => MoveVariantOrder(GetTargetPreset(parameter), -1));

            MoveVariantBackwardCommand = new ActionCommand(
                parameter => GetTargetPreset(parameter)?.CanMoveCurrent(1) == true,
                parameter => MoveVariantOrder(GetTargetPreset(parameter), 1));

            EditTagsCommand = new ActionCommand(
                parameter => GetTargetPreset(parameter) is not null,
                parameter => EditTags(GetTargetPreset(parameter)));

            MovePresetCommand = new ActionCommand(
                parameter => GetTargetEntries(parameter).OfType<TextStylePreset>().Any(),
                parameter => MovePresets([.. GetTargetEntries(parameter).OfType<TextStylePreset>()]));

            NewFolderCommand = new ActionCommand(
                _ => true,
                _ => NewFolder());

            OpenFolderCommand = new ActionCommand(
                parameter => parameter is FolderEntry,
                parameter => OpenFolder(((FolderEntry)parameter!).Path));

            GoUpFolderCommand = new ActionCommand(
                _ => currentFolder.Length > 0,
                _ => OpenFolder(PresetLibrary.GetParentFolder(currentFolder)));

            RenameFolderCommand = new ActionCommand(
                parameter => parameter is FolderEntry,
                parameter => RenameFolder((FolderEntry)parameter!));

            DeleteFolderCommand = new ActionCommand(
                parameter => GetTargetEntries(parameter).Count != 0,
                parameter => DeleteEntries(GetTargetEntries(parameter)));

            AddTemplateCommand = new ActionCommand(
                _ => timeline is not null,
                parameter => AddTemplate(parameter as TemplatePreset));

            RestoreTemplatesCommand = new ActionCommand(
                _ => true,
                _ => RestoreTemplates());

            MergePresetsCommand = new ActionCommand(
                parameter => GetTargetEntries(parameter).OfType<TextStylePreset>().Count() >= 2,
                parameter => MergePresets([.. GetTargetEntries(parameter).OfType<TextStylePreset>()]));

            ChangePresetPreviewTextCommand = new ActionCommand(
                parameter => GetTargetEntries(parameter).OfType<TextStylePreset>().Any(),
                parameter => ChangePresetPreviewText([.. GetTargetEntries(parameter).OfType<TextStylePreset>()]));

            ChangePreviewTextCommand = new ActionCommand(
                _ => true,
                _ => ChangePreviewText());

            OverwritePresetCommand = new ActionCommand(
                parameter => GetTargetPreset(parameter) is not null && SelectedTextItem is not null,
                parameter => OverwritePreset(GetTargetPreset(parameter)));

            EditTemplateFieldsCommand = new ActionCommand(
                parameter => (parameter as TemplatePreset ?? SelectedTemplate) is not null,
                parameter => _ = EditTemplateFieldsAsync(parameter as TemplatePreset ?? SelectedTemplate));

            DuplicatePresetCommand = new ActionCommand(
                parameter => GetTargetEntries(parameter).OfType<TextStylePreset>().Any(),
                parameter => DuplicatePresets([.. GetTargetEntries(parameter).OfType<TextStylePreset>()]));

            ToggleTemplateCanvasCommand = new ActionCommand(
                parameter => GetTargets(parameter, selectedTemplates, SelectedTemplate).Count > 0,
                parameter => ToggleTemplateCanvas(GetTargets(parameter, selectedTemplates, SelectedTemplate)));

            RegisterTemplateAsPresetCommand = new ActionCommand(
                parameter => GetTargets(parameter, selectedTemplates, SelectedTemplate).Any(template => GetTextItem(template) is not null),
                parameter => RegisterTemplatesAsPresets(GetTargets(parameter, selectedTemplates, SelectedTemplate)));

            AddEffectCommand = new ActionCommand(
                _ => SelectedVideoItem is not null,
                parameter => AddEffect(parameter as EffectPreset));

            AddEffectTemplateCommand = new ActionCommand(
                _ => SelectedVideoItem is { VideoEffects.Count: > 0 },
                _ => AddEffectTemplate());

            DeleteEffectTemplateCommand = new ActionCommand(
                parameter => GetTargets(parameter, selectedEffects, SelectedEffect).Any(effect => effect.IsUserTemplate),
                parameter => DeleteEffectTemplates(GetTargets(parameter, selectedEffects, SelectedEffect)));

            EditEffectTemplateCommand = new ActionCommand(
                parameter => GetTargetEffect(parameter) is { IsUserTemplate: true },
                parameter => EditEffectTemplate(GetTargetEffect(parameter)));

            SelectEffectPreviewImageCommand = new ActionCommand(
                _ => true,
                _ => SelectEffectPreviewImage());

            ResetEffectPreviewImageCommand = new ActionCommand(
                _ => PresetStudioSettings.Default.EffectPreviewImagePath.Length > 0,
                _ => SetEffectPreviewImage(string.Empty));

            var effectsView = CollectionViewSource.GetDefaultView(Effects);
            effectsView.Filter = item =>
                item is EffectPreset effect && MatchesSearch(effectSearchText, [effect.Name], effect.Template.Categories);
            effectsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EffectPreset.SourceLabel)));
            CollectionViewSource.GetDefaultView(Templates).Filter = item =>
                item is TemplatePreset template && MatchesSearch(templateSearchText, [template.Name], []);

            Presets.CollectionChanged += Presets_CollectionChanged;
            PresetLibrary.FavoriteChanged += PresetLibrary_FavoriteChanged;
            SettingsBase<ItemSettings>.Default.Templates.CollectionChanged += Templates_CollectionChanged;
            SettingsBase<EffectSettings>.Default.EffectTemplate.CollectionChanged += EffectTemplates_CollectionChanged;
            PresetStudioSettings.Default.EffectTemplates.CollectionChanged += EffectTemplates_CollectionChanged;
            RebuildPresetEntries();
            SyncTemplates();
            SyncEffects();

            Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                TemplateBackupStore.CommitStartupBackup();
                UpdateTemplateWarning();
            }, DispatcherPriority.ApplicationIdle);
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            timeline?.PropertyChanged -= Timeline_PropertyChanged;

            DisposeRenderer();
            timeline = info.Timeline;
            undoRedoManager = info.UndoRedoManager;
            scenes = info.Scenes;
            PresetLinkStore.EnsureScope(scenes);

            if (timeline is null)
            {
                UpdateSelectedItems([]);
                return;
            }

            timeline.PropertyChanged += Timeline_PropertyChanged;
            UpdateSelectedItems(timeline.SelectedItems);

            try
            {
                var videoInfo = PreviewVideoInfo.From(timeline.VideoInfo);
                thumbnailWorker = new RenderWorker(videoInfo, "PresetStudio thumbnail");
                animationWorker = new RenderWorker(videoInfo, "PresetStudio animation");
                RenderThumbnails();
            }
            catch
            {
            }
        }

        private void Timeline_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Timeline.SelectedItems))
                UpdateSelectedItems(timeline?.SelectedItems ?? []);
        }

        private void UpdateSelectedItems(IReadOnlyList<IItem> items)
        {
            selectedItems = items;
            OnPropertyChanged(nameof(SelectedTextItem));
            OnPropertyChanged(nameof(HasSelectedTextItem));
            Fonts?.UpdateCurrent();
            OnPropertyChanged(nameof(SelectedVideoItem));
            CommandManager.InvalidateRequerySuggested();
        }

        private void AddPreset()
        {
            var items = selectedItems.OfType<TextItem>().ToList();
            if (items.Count == 0)
                return;

            var title = items.Count == 1 ? Texts.AddPreset : string.Format(Texts.AddPresets, items.Count);
            if (NameInputViewModel.Show(title, Texts.NameLabel, PresetLibrary.GetNames(), PresetLibrary.GetCategories(), previewText: string.Empty) is not { } input)
                return;

            StopPreviewAnimation();
            var placed = PresetLibrary.PutItems(input.Name, input.Category, currentFolder, items, input.PreviewText);
            if (placed.Count > 0)
                ShowPlacedPreset(placed[^1].Group, placed[^1].Target);
        }

        private void MergePresets(IReadOnlyList<TextStylePreset> presets)
        {
            if (presets.Count < 2)
                return;

            var categories = PresetLibrary.GetCategories().Select(category => (category, string.Empty));
            var initialName = presets.Select(PresetLibrary.GetCategory).FirstOrDefault(category => category.Length > 0) ?? presets[0].Name;
            if (NameInputViewModel.Show(Texts.MergePresets, Texts.CategoryName, categories, initialName: initialName) is not { Name: var category })
                return;

            var targets = presets.ToList();
            if (Presets.FirstOrDefault(preset => !targets.Contains(preset) && PresetLibrary.GetCategory(preset) == category) is { } existing)
                targets.Insert(0, existing);

            StopPreviewAnimation();
            PresetLibrary.Merge(targets, category);
            SavePresets();
        }

        private void ChangePresetPreviewText(IReadOnlyList<TextStylePreset> presets)
        {
            if (presets.Count == 0)
                return;

            var initial = presets.Select(preset => preset.Current.PreviewText ?? string.Empty).Distinct().Count() == 1 ? presets[0].Current.PreviewText ?? string.Empty : string.Empty;
            var placeHolder = string.Format(Texts.PreviewTextEmptyDefault, PresetStudioSettings.Default.PreviewText);
            if (NameInputViewModel.Show(Texts.ChangePreviewText, placeHolder, [], initialName: initial, allowEmpty: true) is not { Name: var text })
                return;

            StopPreviewAnimation();
            foreach (var target in presets.Select(preset => preset.Current))
            {
                target.PreviewText = text;
                pendingThumbnails.Remove(target);
                target.PreviewImage = null;
                RenderThumbnail(target);
            }

            PresetStudioSettings.Default.Save();
        }

        private void ChangePreviewText()
        {
            var settings = PresetStudioSettings.Default;
            if (NameInputViewModel.Show(Texts.ChangeDefaultPreviewText, Texts.DefaultPreviewText, [], initialName: settings.PreviewText) is not { Name: var text } || text == settings.PreviewText)
                return;

            StopPreviewAnimation();
            IEnumerable<IPreviewItem> items = [.. Presets.SelectMany(preset => preset.Variants.Prepend(preset)).Where(preset => preset.PreviewText is null), .. Templates];
            foreach (var item in items)
            {
                pendingThumbnails.Remove(item);
                item.PreviewImage = null;
            }

            settings.PreviewText = text;
            settings.Save();
            RenderThumbnails();
        }

        private void ShowPlacedPreset(TextStylePreset group, TextStylePreset target)
        {
            group.ShowVariant(target);
            RenderThumbnail(target);

            selectedPreset = group;
            OnPropertyChanged(nameof(SelectedPreset));
            CommandManager.InvalidateRequerySuggested();

            SavePresets();
        }

        private void RemovePreset(TextStylePreset preset, TextStylePreset target)
        {
            if (target != preset)
            {
                preset.Variants.Remove(target);
            }
            else if (preset.Variants.Count > 0)
            {
                var next = preset.Variants[0];
                preset.Variants.RemoveAt(0);
                preset.Id = next.Id;
                preset.Name = next.Name;
                preset.Item = next.Item;
                preset.PreviewImage = next.PreviewImage;
                preset.PreviewText = next.PreviewText;
            }
            else
            {
                if (SelectedPreset == preset)
                    SelectedPreset = null;

                Presets.Remove(preset);
            }
        }

        public void ChangeThumbnailScale(int delta)
        {
            var settings = PresetStudioSettings.Default;
            var scale = Math.Clamp(Math.Round(settings.ThumbnailScale + delta * ThumbnailScaleStep, 1), MinThumbnailScale, MaxThumbnailScale);
            if (scale == settings.ThumbnailScale)
                return;

            settings.ThumbnailScale = scale;
            OnPropertyChanged(nameof(ThumbnailWidth));
            OnPropertyChanged(nameof(ThumbnailHeight));
            OnPropertyChanged(nameof(FolderIconSize));
        }

        public void MoveVariant(TextStylePreset preset, int delta)
        {
            if (preset.Variants.Count == 0)
                return;

            var isAnimating = previewAnimation is not null;
            StopPreviewAnimation();
            preset.MoveVariant(delta);
            RenderThumbnail(preset.Current);

            if (SelectedPreset == preset)
                ApplyPreset(preset);

            if (isAnimating)
                StartPreviewAnimation(preset);
        }

        private TextStylePreset? GetTargetPreset(object? parameter) => parameter as TextStylePreset ?? SelectedPreset;

        public void SetSelectedEntries(IEnumerable<object> items)
        {
            selectedEntries = [.. items];
            CommandManager.InvalidateRequerySuggested();
        }

        public void SetSelectedEffects(IEnumerable<object> items)
        {
            selectedEffects = [.. items.OfType<EffectPreset>()];
            CommandManager.InvalidateRequerySuggested();
        }

        public void SetSelectedTemplates(IEnumerable<object> items)
        {
            selectedTemplates = [.. items.OfType<TemplatePreset>()];
            CommandManager.InvalidateRequerySuggested();
        }

        private static List<T> GetTargets<T>(object? parameter, IReadOnlyList<T> selected, T? fallback) where T : class
        {
            if (parameter is T target)
                return selected.Contains(target) ? [.. selected] : [target];

            if (selected.Count > 0)
                return [.. selected];

            return fallback is null ? [] : [fallback];
        }

        private List<object> GetTargetEntries(object? parameter) =>
            [.. GetTargets(parameter, selectedEntries, SelectedPreset).Where(entry => entry is TextStylePreset or FolderEntry)];

        private void EditPreset(TextStylePreset? preset)
        {
            if (preset is null)
                return;

            var target = preset.Current;
            var currentCategory = PresetLibrary.GetCategory(preset);
            if (NameInputViewModel.Show(Texts.EditPreset, Texts.NameLabel, PresetLibrary.GetNames(), PresetLibrary.GetCategories(), target.Name, currentCategory, previewText: target.PreviewText ?? string.Empty) is not { } input)
                return;

            if (input.PreviewText != target.PreviewText)
            {
                StopPreviewAnimation();
                target.PreviewText = input.PreviewText;
                target.PreviewImage = null;
                RenderThumbnail(target);
            }

            if (input.Category == currentCategory)
            {
                target.Name = input.Name;
                preset.RefreshDisplayName();
                SavePresets();
                return;
            }

            StopPreviewAnimation();
            var item = target.Item;
            var previewImage = target.PreviewImage;
            var excluded = preset.Variants.Count == 0 ? preset : null;
            if (PresetLibrary.Put(input.Name, input.Category, preset.Folder, item, previewImage, excluded) is not { } placed)
                return;

            placed.Target.PreviewText = input.PreviewText;
            var id = target.Id;
            RemovePreset(preset, target);
            placed.Target.Id = id;
            ShowPlacedPreset(placed.Group, placed.Target);
        }

        private void MoveVariantOrder(TextStylePreset? preset, int delta)
        {
            if (preset is null || !preset.CanMoveCurrent(delta))
                return;

            StopPreviewAnimation();
            preset.MoveCurrent(delta);
            SavePresets();
        }

        private void EditCategory(TextStylePreset? preset)
        {
            if (preset is null || PresetLibrary.GetCategory(preset) is not { Length: > 0 } currentCategory)
                return;

            if (NameInputViewModel.Show(Texts.EditCategoryName, Texts.Category, [], initialName: currentCategory) is not { Name: var category } || category == currentCategory)
                return;

            if (Presets.Any(other => other != preset && PresetLibrary.GetCategory(other) == category))
            {
                MessageWindow.Inform(string.Format(Texts.CategoryExists, category));
                return;
            }

            preset.Category = category;
            SavePresets();
        }

        public void ApplyPresetParts(TextStylePreset preset, TextStyleParts parts)
        {
            if (preset.Current.Item is not { } || selectedItems.Count == 0)
                return;

            var applied = false;
            foreach (var target in selectedItems)
            {
                applied |= PresetLinkStore.ApplyPreset(target, preset.Current, parts);
            }

            if (applied)
                undoRedoManager?.Record();
        }

        private void OverwritePreset(TextStylePreset? preset)
        {
            if (preset is null || SelectedTextItem is not { } item)
                return;

            var target = preset.Current;
            if (!PresetLibrary.Confirm(string.Format(Texts.ConfirmOverwriteWithSelected, target.Name)))
                return;

            var oldStyle = (TextItem)target.Item.GetClone();
            var linked = PresetLinkStore.FindLinked(AllTimelines, target).Where(linkedItem => linkedItem != item).ToList();

            StopPreviewAnimation();
            PresetLibrary.Overwrite(target, item);
            RenderThumbnail(target);
            PresetLinkStore.Link(item, target, TextStyleParts.All);

            if (linked.Count > 0 && PresetLibrary.Confirm(string.Format(Texts.ConfirmUpdateLinked, target.Name, linked.Count)))
            {
                foreach (var linkedItem in linked)
                    TextStyleApplier.ReplaceOnItem(linkedItem, oldStyle, target.Item, PresetLinkStore.GetLink(linkedItem)?.Parts ?? TextStyleApplier.DefaultParts);
                undoRedoManager?.Record();
            }
        }

        private IEnumerable<Timeline> AllTimelines => scenes?.Timelines ?? (timeline is null ? [] : [timeline]);

        private void DuplicatePresets(IReadOnlyList<TextStylePreset> presets)
        {
            if (presets.Count == 0)
                return;

            StopPreviewAnimation();
            var placed = presets.Select(PresetLibrary.Duplicate).ToList();
            if (placed.Count == 1)
                ShowPlacedPreset(placed[0].Group, placed[0].Target);
            else
                SavePresets();
        }

        private void DeleteEntries(IReadOnlyList<object> entries)
        {
            var presets = entries.OfType<TextStylePreset>().ToList();
            var folders = entries.OfType<FolderEntry>().ToList();
            var message = (presets.Count, folders.Count) switch
            {
                (1, 0) => string.Format(Texts.ConfirmDelete, presets[0].Current.Name),
                (0, 1) => string.Format(Texts.ConfirmDeleteFolder, folders[0].Name),
                _ => string.Format(Texts.ConfirmDeleteCount, presets.Count + folders.Count),
            };
            if (folders.Count > 0)
                message += Environment.NewLine + Texts.DeleteFolderNote;

            if ((presets.Count == 0 && folders.Count == 0) || !PresetLibrary.Confirm(message))
                return;

            StopPreviewAnimation();
            foreach (var preset in presets)
                RemovePreset(preset, preset.Current);

            foreach (var folder in folders)
                MoveFolderContents(folder.Path, PresetLibrary.GetParentFolder(folder.Path));

            SavePresets();
        }

        private void SavePresets()
        {
            PresetStudioSettings.Default.Save();
            RebuildPresetEntries();
        }

        private void Presets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPresetEntries();

        private void PresetLibrary_FavoriteChanged(object? sender, EventArgs e) => RebuildPresetEntries();

        private void RebuildPresetEntries()
        {
            StopPreviewAnimation();
            isRebuildingEntries = true;
            try
            {
                PresetEntries.Clear();
                if (!string.IsNullOrWhiteSpace(presetSearchText))
                {
                    foreach (var preset in Presets.Where(MatchesPreset).OrderByDescending(preset => preset.IsFavorite))
                        PresetEntries.Add(preset);
                }
                else
                {
                    var folders = PresetLibrary.GetAllFolders()
                        .Where(folder => PresetLibrary.GetParentFolder(folder) == currentFolder)
                        .OrderBy(PresetLibrary.GetFolderName, nameComparer);
                    foreach (var folder in folders)
                        PresetEntries.Add(new FolderEntry(PresetLibrary.GetFolderName(folder), folder));

                    foreach (var preset in Presets.Where(preset => preset.Folder == currentFolder).OrderByDescending(preset => preset.IsFavorite))
                        PresetEntries.Add(preset);
                }
            }
            finally
            {
                isRebuildingEntries = false;
            }

            if (selectedPreset is not null && !PresetEntries.Contains(selectedPreset))
            {
                selectedPreset = null;
                OnPropertyChanged(nameof(SelectedPreset));
            }

            selectedEntry = selectedPreset;
            OnPropertyChanged(nameof(SelectedEntry));
            CommandManager.InvalidateRequerySuggested();
        }

        private bool MatchesPreset(TextStylePreset preset) =>
            MatchesSearch(presetSearchText, [preset.Name, preset.Category ?? string.Empty, preset.Folder, .. preset.Variants.Select(variant => variant.Name)], preset.Tags);

        private static bool MatchesSearch(string search, IEnumerable<string> texts, IEnumerable<string> tags)
        {
            foreach (var token in search.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith('#'))
                {
                    var tag = token[1..];
                    if (tag.Length > 0 && !tags.Any(x => x.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }
                else if (!texts.Concat(tags).Any(x => x.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CombineFolder(string parent, string name) => parent.Length == 0 ? name : $"{parent}/{name}";

        private static bool IsInFolder(string path, string folder) => path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);

        private static string ReplaceFolder(string path, string from, string to) => path == from ? to : CombineFolder(to, path[(from.Length + 1)..]);

        private void OpenFolder(string path)
        {
            CurrentFolder = path;
            if (presetSearchText.Length > 0)
            {
                presetSearchText = string.Empty;
                OnPropertyChanged(nameof(PresetSearchText));
            }

            RebuildPresetEntries();
        }

        private void NewFolder()
        {
            if (NameInputViewModel.Show(Texts.NewFolder, Texts.FolderName, []) is not { Name: var name } || PresetLibrary.NormalizeFolder(name) is not { Length: > 0 } normalized)
                return;

            var path = CombineFolder(currentFolder, normalized);
            if (!PresetStudioSettings.Default.PresetFolders.Contains(path))
                PresetStudioSettings.Default.PresetFolders.Add(path);

            SavePresets();
        }

        private void RenameFolder(FolderEntry folder)
        {
            if (NameInputViewModel.Show(Texts.RenameFolder, Texts.FolderName, [], initialName: folder.Name) is not { Name: var name } || PresetLibrary.NormalizeFolder(name) is not { Length: > 0 } normalized)
                return;

            var path = CombineFolder(PresetLibrary.GetParentFolder(folder.Path), normalized);
            if (path == folder.Path)
                return;

            if (PresetLibrary.GetAllFolders().Contains(path))
            {
                MessageWindow.Inform(string.Format(Texts.FolderExists, normalized));
                return;
            }

            MoveFolderContents(folder.Path, path);
            SavePresets();
        }

        private static void MoveFolderContents(string from, string to)
        {
            foreach (var preset in Presets.Where(preset => IsInFolder(preset.Folder, from)))
                preset.Folder = ReplaceFolder(preset.Folder, from, to);

            var folders = PresetStudioSettings.Default.PresetFolders;
            var moved = folders
                .Select(folder => IsInFolder(folder, from) ? ReplaceFolder(folder, from, to) : folder)
                .Where(folder => folder.Length > 0)
                .Distinct()
                .ToList();
            folders.Clear();
            foreach (var folder in moved)
                folders.Add(folder);
        }

        private void MovePresets(IReadOnlyList<TextStylePreset> presets)
        {
            if (presets.Count == 0)
                return;

            var folders = PresetLibrary.GetAllFolders().OrderBy(folder => folder, nameComparer).Select(folder => (folder, string.Empty));
            var initialFolder = presets.Select(preset => preset.Folder).Distinct().Count() == 1 ? presets[0].Folder : currentFolder;
            if (NameInputViewModel.Show(Texts.MoveToFolder, Texts.Folder, folders, initialName: initialFolder, allowEmpty: true) is not { Name: var name })
                return;

            var folder = PresetLibrary.NormalizeFolder(name);
            foreach (var preset in presets)
                preset.Folder = folder;
            if (folder.Length > 0 && !PresetStudioSettings.Default.PresetFolders.Contains(folder))
                PresetStudioSettings.Default.PresetFolders.Add(folder);

            SavePresets();
        }

        private void EditTags(TextStylePreset? preset)
        {
            if (preset is null)
                return;

            var allTags = Presets
                .SelectMany(item => item.Variants.Prepend(item))
                .SelectMany(item => item.Tags)
                .Distinct()
                .Order(nameComparer)
                .ToList();
            if (TagInputViewModel.Show(Texts.EditTags, string.Join(" ", preset.Tags), allTags) is not { } text)
                return;

            preset.Tags = TagInputViewModel.ParseTags(text);
            SavePresets();
        }

        private void ApplyPreset(TextStylePreset? preset)
        {
            if (preset?.Current.Item is not { } || selectedItems.Count == 0)
                return;

            var parts = TextStyleApplier.CurrentParts;
            var applied = false;
            foreach (var target in selectedItems)
            {
                applied |= PresetLinkStore.ApplyPreset(target, preset.Current, parts);
            }

            if (applied)
                undoRedoManager?.Record();
        }

        private void Templates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncTemplates();

        private void Template_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ItemTemplate.Name) or null)
                SyncTemplates();
        }

        private void SyncTemplates()
        {
            foreach (var template in Templates)
                template.Template.PropertyChanged -= Template_PropertyChanged;

            var existing = Templates.ToDictionary(x => x.Template);
            var selected = SelectedTemplate?.Template;

            Templates.Clear();
            foreach (var template in SettingsBase<ItemSettings>.Default.SortedTemplates)
            {
                template.PropertyChanged += Template_PropertyChanged;
                var preset = existing.GetValueOrDefault(template) ?? new TemplatePreset(template);
                preset.IsFullCanvas = PresetStudioSettings.Default.FullCanvasTemplates.Contains(preset.Key);
                preset.FieldCount = GetTemplateFields(preset).Count;
                Templates.Add(preset);
            }

            SelectedTemplate = Templates.FirstOrDefault(x => x.Template == selected);
            UpdateTemplateWarning();
            RenderThumbnails();
        }

        private async void AddTemplate(TemplatePreset? preset)
        {
            if (timeline is not { } target || preset is null || isAddingTemplate)
                return;

            var targetUndoRedoManager = undoRedoManager;
            isAddingTemplate = true;
            try
            {
                if (await CreateTemplateItemsAsync(preset, target.VideoInfo.FPS) is not { } items)
                    return;

                if (target.TryAddItems([.. items], target.CurrentFrame, preset.Template.Layer))
                    targetUndoRedoManager?.Record();
            }
            catch
            {
                MessageWindow.Inform(string.Format(Texts.AddFailed, preset.Name));
            }
            finally
            {
                isAddingTemplate = false;
            }
        }

        private static List<TemplateField> GetTemplateFields(TemplatePreset preset) =>
            PresetStudioSettings.Default.TemplateFields.FirstOrDefault(set => set.Key == preset.Key)?.Fields ?? [];

        public static async Task<IReadOnlyList<IItem>?> CreateTemplateItemsAsync(TemplatePreset preset, int fps)
        {
            var items = (await preset.Template.CreateItemsAsync(fps)).ToList();
            var fields = GetTemplateFields(preset);
            if (fields.Count == 0)
                return items;

            var textItems = items.OfType<TextItem>().ToList();
            return TemplateInputViewModel.Show(preset.Name, textItems, fields) ? items : null;
        }

        private async Task EditTemplateFieldsAsync(TemplatePreset? preset)
        {
            if (preset is null)
                return;

            List<TextItem> textItems;
            try
            {
                textItems = [.. (await preset.Template.CreateItemsAsync(timeline?.VideoInfo.FPS ?? 30)).OfType<TextItem>()];
            }
            catch
            {
                MessageWindow.Inform(string.Format(Texts.LoadFailed, preset.Name));
                return;
            }

            if (textItems.Count == 0)
            {
                MessageWindow.Inform(Texts.TemplateHasNoTextItems);
                return;
            }

            if (TemplateFieldsViewModel.Show(textItems, GetTemplateFields(preset)) is not { } fields)
                return;

            var sets = PresetStudioSettings.Default.TemplateFields;
            if (sets.FirstOrDefault(set => set.Key == preset.Key) is { } existing)
                sets.Remove(existing);
            if (fields.Count > 0)
                sets.Add(new TemplateFieldSet { Key = preset.Key, Fields = fields });

            PresetStudioSettings.Default.Save();
            preset.FieldCount = fields.Count;
        }

        private void UpdateTemplateWarning()
        {
            var templates = SettingsBase<ItemSettings>.Default.Templates;
            var messages = new List<string>();

            var notLoadedCount = TemplateBackupStore.CountNotLoadedTemplates(templates);
            if (notLoadedCount > 0)
                messages.Add(string.Format(Texts.TemplatesNotLoaded, notLoadedCount));

            var lostCount = TemplateBackupStore.LostTemplateNames.Count(name => templates.All(template => template.Name != name));
            if (lostCount > 0)
                messages.Add(string.Format(Texts.TemplatesLost, lostCount));

            var hiddenCount = templates.Count(template => template.Path.Length == 0);
            if (hiddenCount > 0)
                messages.Add(string.Format(Texts.TemplatesHidden, hiddenCount));

            TemplateWarning = messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
        }

        private static void RestoreTemplates()
        {
            var settings = SettingsBase<ItemSettings>.Default;
            var candidates = TemplateBackupStore.FindRestorableTemplates(settings.Templates);
            if (candidates.Count == 0)
            {
                MessageWindow.Inform(Texts.NoRestorableTemplates);
                return;
            }

            var dialog = new TemplateRestoreWindow(candidates)
            {
                Owner = Application.Current?.MainWindow,
            };

            if (dialog.ShowDialog() != true)
                return;

            foreach (var candidate in candidates.Where(x => x.IsChecked && x.CanRestore))
            {
                if (TemplateBackupStore.CreateTemplate(candidate) is not { } template)
                    continue;

                var existing = settings.Templates.FirstOrDefault(x => x.Name == candidate.Name);
                if (existing is null)
                    settings.Templates.Add(template);
                else
                    settings.Templates[settings.Templates.IndexOf(existing)] = template;
            }

            settings.Save();
        }

        private void EffectTemplates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncEffects();

        private void EffectTemplate_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(EffectTemplate<>.Name) or null)
                SyncEffects();
        }

        private void SyncEffects()
        {
            foreach (var effect in Effects)
                effect.Template.PropertyChanged -= EffectTemplate_PropertyChanged;

            var existing = Effects.ToDictionary(x => x.Template);
            var selected = SelectedEffect?.Template;

            IEnumerable<(EffectTemplate<IVideoEffect> Template, EffectSource Source)> templates =
            [
                .. PresetStudioSettings.Default.EffectTemplates.OrderBy(x => x.Name ?? string.Empty, nameComparer).Select(x => (x, EffectSource.User)),
                .. SettingsBase<EffectSettings>.Default.EffectTemplate.OrderBy(x => x.Name ?? string.Empty, nameComparer).Select(x => (x, EffectSource.Template)),
            ];

            Effects.Clear();
            foreach (var (template, source) in templates)
            {
                template.PropertyChanged += EffectTemplate_PropertyChanged;
                Effects.Add(existing.GetValueOrDefault(template) ?? new EffectPreset(template, source, source == EffectSource.User ? Texts.PluginName : Texts.EffectTemplateSource));
            }

            defaultEffects ??= CreateDefaultEffects();
            foreach (var effect in defaultEffects)
                Effects.Add(effect);

            SelectedEffect = Effects.FirstOrDefault(x => x.Template == selected);
            RenderThumbnails();
        }

        private EffectPreset? GetTargetEffect(object? parameter) => parameter as EffectPreset ?? SelectedEffect;

        private void AddEffect(EffectPreset? preset)
        {
            if (preset is null)
                return;

            var targets = selectedItems.OfType<IVideoItem>().ToList();
            if (targets.Count == 0)
                return;

            foreach (var item in targets)
                item.VideoEffects = item.VideoEffects.AddRange(preset.Template.CreateEffects());

            undoRedoManager?.Record();
        }

        private void AddEffectTemplate()
        {
            if (SelectedVideoItem is not { VideoEffects.Count: > 0 } item)
                return;

            var templates = PresetStudioSettings.Default.EffectTemplates;
            var names = templates.Select(template => (template.Name ?? string.Empty, string.Empty));
            if (NameInputViewModel.Show(Texts.AddEffectTemplate, Texts.TemplateName, names) is not { Name: var name })
                return;

            if (templates.FirstOrDefault(template => template.Name == name) is { } existing)
            {
                if (!PresetLibrary.Confirm(string.Format(Texts.ConfirmOverwrite, name)))
                    return;

                StopPreviewAnimation();
                templates.Remove(existing);
            }

            templates.Add(new EffectTemplate<IVideoEffect>(name, item.VideoEffects));
            SelectedEffect = Effects.FirstOrDefault(x => x.IsUserTemplate && x.Template.Name == name);
            SavePresets();
        }

        private void EditEffectTemplate(EffectPreset? preset)
        {
            if (preset is not { IsUserTemplate: true } || NameInputViewModel.Show(Texts.EditEffectTemplate, Texts.TemplateName, [], initialName: preset.Name) is not { Name: var name })
                return;

            preset.Template.Name = name;
            SavePresets();
        }

        private void DeleteEffectTemplates(IReadOnlyList<EffectPreset> effects)
        {
            var targets = effects.Where(effect => effect.IsUserTemplate).ToList();
            if (targets.Count == 0)
                return;

            var message = targets.Count == 1 ? string.Format(Texts.ConfirmDelete, targets[0].Name) : string.Format(Texts.ConfirmDeleteCount, targets.Count);
            if (!PresetLibrary.Confirm(message))
                return;

            StopPreviewAnimation();
            foreach (var effect in targets)
                PresetStudioSettings.Default.EffectTemplates.Remove(effect.Template);
            SavePresets();
        }

        private void RuleImages_SampleImagesChanged()
        {
            StopPreviewAnimation();
            foreach (var transition in RuleImages.Rules.OfType<TransitionPreset>())
            {
                pendingThumbnails.Remove(transition);
                transition.PreviewImage = null;
            }

            RenderThumbnails();
        }

        private void ToggleTemplateCanvas(List<TemplatePreset> templates)
        {
            if (templates.Count == 0)
                return;

            var isFullCanvas = !templates[0].IsFullCanvas;
            var keys = PresetStudioSettings.Default.FullCanvasTemplates;
            StopPreviewAnimation();
            foreach (var template in templates)
            {
                template.IsFullCanvas = isFullCanvas;
                keys.Remove(template.Key);
                if (isFullCanvas)
                    keys.Add(template.Key);

                pendingThumbnails.Remove(template);
                template.PreviewImage = null;
                RenderThumbnail(template);
            }

            PresetStudioSettings.Default.Save();
        }

        private static TextItem? GetTextItem(TemplatePreset template) => template.Template.Items.OfType<TextItem>().FirstOrDefault();

        private void RegisterTemplatesAsPresets(IReadOnlyList<TemplatePreset> templates)
        {
            var targets = templates
                .Select(template => (Template: template, Item: GetTextItem(template)))
                .Where(target => target.Item is not null)
                .ToList();
            if (targets.Count == 0)
                return;

            StopPreviewAnimation();
            if (targets.Count == 1)
            {
                var (template, item) = targets[0];
                if (NameInputViewModel.Show(Texts.RegisterAsTextPreset, Texts.NameLabel, PresetLibrary.GetNames(), PresetLibrary.GetCategories(), initialName: template.Template.Name ?? string.Empty, previewText: string.Empty) is not { } input)
                    return;

                if (PresetLibrary.Put(input.Name, input.Category, currentFolder, (TextItem)item!.GetClone(), null) is { } placed)
                {
                    placed.Target.PreviewText = input.PreviewText;
                    ShowPlacedPreset(placed.Group, placed.Target);
                }
                return;
            }

            foreach (var (template, item) in targets)
            {
                var name = string.IsNullOrWhiteSpace(template.Template.Name) ? Texts.NewPreset : template.Template.Name.Trim();
                PresetLibrary.Put(name, string.Empty, currentFolder, (TextItem)item!.GetClone(), null);
            }

            SavePresets();
        }

        private static readonly string[] defaultEffectCategoryOrder =
        [
            VideoEffectCategories.Animation,
            VideoEffectCategories.Filtering,
            VideoEffectCategories.Decoration,
            VideoEffectCategories.Drawing,
            VideoEffectCategories.Transition,
            VideoEffectCategories.Camera,
            VideoEffectCategories.Composition,
            VideoEffectCategories.Layout,
            VideoEffectCategories.Branch,
        ];

        private static List<EffectPreset> CreateDefaultEffects()
        {
            var result = new List<EffectPreset>();
            try
            {
                var categoryOrder = defaultEffectCategoryOrder.Select(GetCategoryName).ToList();
                var groups = EffectFactories.VideoEffectFactories
                    .Where(factory => !factory.IsUserEffect && !factory.IsObsolete)
                    .GroupBy(factory => factory.DefaultCategories.FirstOrDefault() is { Length: > 0 } category ? category : Texts.Other)
                    .OrderBy(group => categoryOrder.IndexOf(group.Key) is var index and >= 0 ? index : int.MaxValue)
                    .ThenBy(group => group.Key, nameComparer);

                foreach (var group in groups)
                {
                    var categoryName = group.Key;
                    foreach (var factory in group.OrderBy(factory => factory.Name, nameComparer))
                    {
                        try
                        {
                            var template = new EffectTemplate<IVideoEffect>(factory.Name, [factory.Create()])
                            {
                                Categories = [categoryName],
                            };
                            result.Add(new EffectPreset(template, EffectSource.Default, string.Format(Texts.DefaultCategory, categoryName)));
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static string GetCategoryName(string key)
        {
            const string KeyPrefix = "YMM4Key_";
            return Texts.GetString(key.StartsWith(KeyPrefix, StringComparison.Ordinal) ? key[KeyPrefix.Length..] : key) ?? key;
        }

        private void SelectEffectPreviewImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = Texts.SelectPreviewImage,
                Filter = Texts.ImageFileFilter,
            };

            if (dialog.ShowDialog(Application.Current?.MainWindow) == true)
                SetEffectPreviewImage(dialog.FileName);
        }

        private void SetEffectPreviewImage(string path)
        {
            PresetStudioSettings.Default.EffectPreviewImagePath = path;
            PresetStudioSettings.Default.Save();

            StopPreviewAnimation();
            foreach (var effect in Effects)
            {
                pendingThumbnails.Remove(effect);
                effect.PreviewImage = null;
            }

            RenderThumbnails();
            CommandManager.InvalidateRequerySuggested();
        }

        public void UpdateVisibleItems(object source, IReadOnlyList<IPreviewItem> items)
        {
            visibleItemsBySource[source] = items;
            wantedThumbnails = new HashSet<IPreviewItem>(visibleItemsBySource.Values.SelectMany(x => x), ReferenceEqualityComparer.Instance);

            foreach (var item in items)
            {
                ThumbnailCache.Touch(item);
                RenderThumbnail(item, force: false);
            }
        }

        private void RenderThumbnails()
        {
            foreach (var item in wantedThumbnails)
                RenderThumbnail(item, force: false);
        }

        private void RenderThumbnail(IPreviewItem item, bool force = true)
        {
            if (item is RuleImagePreset rule)
            {
                RuleImages.LoadThumbnail(rule);
                return;
            }

            if (item is FacePartOption)
                return;

            if (thumbnailWorker is { } worker && item.PreviewImage is null && pendingThumbnails.Add(item))
                _ = RenderThumbnailAsync(worker, item, force);
        }

        private async Task RenderThumbnailAsync(RenderWorker worker, IPreviewItem item, bool force)
        {
            try
            {
                var text = GetPreviewText(item);
                var useCanvas = item is TemplatePreset { IsFullCanvas: true };
                var trims = item is FacePreset or TachieItemPreset;
                var createItems = PrepareItemsFactory(item, worker.VideoInfo);
                var image = await worker.Enqueue(renderer =>
                    (force || wantedThumbnails.Contains(item)) && createItems() is { } items
                        ? useCanvas ? renderer.RenderCanvas(items, null)
                        : trims ? renderer.Render(items, null, renderer.MeasureOpaqueBounds(items))
                        : renderer.Render(items, null)
                        : null);

                if (image is not null && worker == thumbnailWorker && item.PreviewImage is null && text == GetPreviewText(item))
                {
                    item.PreviewImage = image;
                    ThumbnailCache.Add(item);
                }
            }
            catch
            {
            }
            finally
            {
                if (worker == thumbnailWorker)
                    pendingThumbnails.Remove(item);
            }
        }

        private static Func<IReadOnlyList<IItem>?> PrepareItemsFactory(IPreviewItem item, PreviewVideoInfo videoInfo)
        {
            if (item is EffectPreset)
                EffectSampleImage.GetPath();
            if (item is TransitionPreset)
                TransitionSampleImage.GetPaths();

            var text = GetPreviewText(item);
            var character = item switch
            {
                FacePreset face => CharacterProvider.Find(face.CharacterName, null),
                TachieItemPreset tachie => CharacterProvider.Find(tachie.CharacterName, null),
                _ => null,
            };
            return () => CreatePreviewItems(item, videoInfo, text, character);
        }

        private static string GetPreviewText(IPreviewItem item) =>
            item is TextStylePreset preset ? PresetLibrary.GetPreviewText(preset) : PresetStudioSettings.Default.PreviewText;

        private static IReadOnlyList<IItem>? CreatePreviewItems(IPreviewItem item, PreviewVideoInfo videoInfo, string text, Character? character) => item switch
        {
            FacePreset face when character is not null => PresetPreviewRenderer.CreateTachiePreviewItems(character, null, face, videoInfo.FPS),
            TachieItemPreset tachie when character is not null => PresetPreviewRenderer.CreateTachiePreviewItems(character, tachie.Item, null, videoInfo.FPS),
            TextStylePreset preset => PresetPreviewRenderer.CreatePreviewItems([preset.Item], text),
            TemplatePreset template => PresetPreviewRenderer.CreatePreviewItems(template.Template.Items, text),
            EffectPreset effect => PresetPreviewRenderer.CreateEffectPreviewItems(effect.Template.CreateEffects(), videoInfo.FPS),
            TransitionPreset transition => PresetPreviewRenderer.CreateTransitionPreviewItems(transition.Plugin, videoInfo),
            _ => null,
        };

        public void StartPreviewAnimation(IPreviewItem target)
        {
            StopPreviewAnimation();
            if (animationWorker is not { } worker)
                return;

            if (target is TextStylePreset preset)
                target = preset.Current;

            try
            {
                previewAnimation = new PreviewAnimation(worker, target, PrepareItemsFactory(target, worker.VideoInfo), target is TemplatePreset { IsFullCanvas: true }, target is FacePreset or TachieItemPreset);
            }
            catch
            {
            }
        }

        public void StopPreviewAnimation()
        {
            if (previewAnimation is not { } animation)
                return;

            previewAnimation = null;
            animation.Dispose();
            RenderThumbnail(animation.Target);
        }

        public void Dispose()
        {
            ThumbnailCache.Unregister(this);
            RuleImages.Dispose();
            Presets.CollectionChanged -= Presets_CollectionChanged;
            PresetLibrary.FavoriteChanged -= PresetLibrary_FavoriteChanged;
            SettingsBase<ItemSettings>.Default.Templates.CollectionChanged -= Templates_CollectionChanged;
            foreach (var template in Templates)
                template.Template.PropertyChanged -= Template_PropertyChanged;

            SettingsBase<EffectSettings>.Default.EffectTemplate.CollectionChanged -= EffectTemplates_CollectionChanged;
            PresetStudioSettings.Default.EffectTemplates.CollectionChanged -= EffectTemplates_CollectionChanged;
            AudioEffects.Dispose();
            foreach (var effect in Effects)
                effect.Template.PropertyChanged -= EffectTemplate_PropertyChanged;

            timeline?.PropertyChanged -= Timeline_PropertyChanged;

            timeline = null;
            undoRedoManager = null;
            scenes = null;
            selectedItems = [];
            DisposeRenderer();
        }

        private void DisposeRenderer()
        {
            pendingThumbnails.Clear();
            StopPreviewAnimation();
            animationWorker?.Dispose();
            animationWorker = null;
            thumbnailWorker?.Dispose();
            thumbnailWorker = null;
        }
    }
}
