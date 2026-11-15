using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PresetStudio.Model;
using PresetStudio.Preview;
using PresetStudio.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Plugin.Transition;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.Transition;

namespace PresetStudio.ViewModel
{
    internal sealed class RuleImageTabViewModel : Bindable, IDisposable
    {
        private const int ThumbnailSize = 160;

        private static readonly NaturalComparer nameComparer = new();

        private readonly Func<Timeline?> getTimeline;
        private readonly Func<IReadOnlyList<IItem>> getSelectedItems;
        private List<TransitionPreset>? transitions;
        private readonly Action recordUndo;
        private IReadOnlyList<ITransitionEntry> selectedRules = [];
        private RuleImageAnimation? animation;
        private readonly Dictionary<string, (DateTime WriteTime, RuleImagePreset Rule)> ruleCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<RuleImagePreset> pendingThumbnails = new(ReferenceEqualityComparer.Instance);

        public ObservableCollection<ITransitionEntry> Rules { get; } = [];

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                if (Set(ref searchText, value ?? string.Empty))
                    CollectionViewSource.GetDefaultView(Rules).Refresh();
            }
        }

        private ITransitionEntry? selectedRule;
        public ITransitionEntry? SelectedRule
        {
            get => selectedRule;
            set
            {
                if (Set(ref selectedRule, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public ActionCommand CreateCommand { get; }

        public ActionCommand EditCommand { get; }

        public ActionCommand DeleteCommand { get; }

        public ActionCommand ApplyCommand { get; }

        public ActionCommand OpenFolderCommand { get; }

        public ActionCommand SelectBeforeImageCommand { get; }

        public ActionCommand SelectAfterImageCommand { get; }

        public ActionCommand ResetSampleImagesCommand { get; }

        public event Action? SampleImagesChanged;

        private static string UserTransitionDirectory => Path.Combine(AppDirectories.UserDirectory, "transition");

        private static string BundledTransitionDirectory => Path.Combine(AppDirectories.ResourceDirectory, "Transition");

        private static ObservableCollection<RuleImageRecord> Records => PresetStudioSettings.Default.RuleImages;

        public RuleImageTabViewModel(Func<Timeline?> getTimeline, Func<IReadOnlyList<IItem>> getSelectedItems, Action recordUndo)
        {
            this.getTimeline = getTimeline;
            this.getSelectedItems = getSelectedItems;
            this.recordUndo = recordUndo;

            CreateCommand = new ActionCommand(_ => true, _ => Edit(null));
            EditCommand = new ActionCommand(
                parameter => GetTargets(parameter) is [RuleImagePreset { IsOwned: true }],
                parameter => Edit((RuleImagePreset)GetTargets(parameter)[0]));
            DeleteCommand = new ActionCommand(
                parameter => GetTargets(parameter).OfType<RuleImagePreset>().Any(rule => rule.IsOwned),
                parameter => Delete([.. GetTargets(parameter).OfType<RuleImagePreset>()]));
            ApplyCommand = new ActionCommand(
                parameter => (parameter as ITransitionEntry ?? SelectedRule) is not null && getTimeline() is not null,
                parameter => Apply(parameter as ITransitionEntry ?? SelectedRule));
            OpenFolderCommand = new ActionCommand(_ => true, _ => OpenFolder());
            SelectBeforeImageCommand = new ActionCommand(_ => true, _ => SelectSampleImage(isBefore: true));
            SelectAfterImageCommand = new ActionCommand(_ => true, _ => SelectSampleImage(isBefore: false));
            ResetSampleImagesCommand = new ActionCommand(
                _ => PresetStudioSettings.Default.TransitionBeforeImagePath.Length > 0 || PresetStudioSettings.Default.TransitionAfterImagePath.Length > 0,
                _ => SetSampleImages(string.Empty, string.Empty));

            var view = CollectionViewSource.GetDefaultView(Rules);
            view.Filter = item => item is ITransitionEntry entry && Matches(entry);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ITransitionEntry.SourceLabel)));

            Refresh();
        }

        private bool Matches(ITransitionEntry entry) =>
            searchText.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries).All(token => entry.Name.Contains(token, StringComparison.OrdinalIgnoreCase));

        public void SetSelected(IEnumerable<object> items)
        {
            selectedRules = [.. items.OfType<ITransitionEntry>()];
            CommandManager.InvalidateRequerySuggested();
        }

        private List<ITransitionEntry> GetTargets(object? parameter)
        {
            if (parameter is ITransitionEntry target)
                return selectedRules.Contains(target) ? [.. selectedRules] : [target];

            if (selectedRules.Count > 0)
                return [.. selectedRules];

            return SelectedRule is null ? [] : [SelectedRule];
        }

        public void Refresh()
        {
            StopAnimation();
            var owned = Records
                .Where(record => File.Exists(record.Path))
                .Select(record => GetRule(record.Path, record.Name, RuleImageSource.PresetStudio))
                .OrderBy(rule => rule.Name, nameComparer);
            var ownedPaths = Records.Select(record => Path.GetFullPath(record.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var user = GetFiles(UserTransitionDirectory)
                .Where(path => !ownedPaths.Contains(path))
                .Select(path => GetRule(path, Path.GetFileNameWithoutExtension(path), RuleImageSource.User));
            var bundled = GetFiles(BundledTransitionDirectory)
                .Where(path => !ownedPaths.Contains(path))
                .Select(path => GetRule(path, Path.GetFileNameWithoutExtension(path), RuleImageSource.Bundled));

            transitions ??= CreateTransitions();

            Rules.Clear();
            foreach (var transition in transitions)
                Rules.Add(transition);

            foreach (var rule in owned.Concat(user).Concat(bundled))
                Rules.Add(rule);
        }

        private RuleImagePreset GetRule(string path, string name, RuleImageSource source)
        {
            var writeTime = GetWriteTime(path);
            if (ruleCache.TryGetValue(path, out var cached) && cached.WriteTime == writeTime && cached.Rule.Name == name && cached.Rule.Source == source)
                return cached.Rule;

            var rule = new RuleImagePreset(path, name, source);
            ruleCache[path] = (writeTime, rule);
            return rule;
        }

        private static DateTime GetWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static List<TransitionPreset> CreateTransitions()
        {
            try
            {
                return
                [
                    .. TransitionFactory.OrderdPlugins
                        .Where(plugin => plugin.GetType().Name is not ("RuleImageTransitionPlugin" or "NoneTransitionPlugin"))
                        .Where(plugin => !Attribute.IsDefined(plugin.GetType(), typeof(ObsoleteAttribute)))
                        .Select(plugin => new TransitionPreset(plugin)),
                ];
            }
            catch
            {
                return [];
            }
        }

        private static IEnumerable<string> GetFiles(string directory)
        {
            try
            {
                return Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "*.png").Select(Path.GetFullPath).OrderBy(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty, nameComparer)
                    : [];
            }
            catch
            {
                return [];
            }
        }

        public void LoadThumbnail(RuleImagePreset rule)
        {
            if (rule.PreviewImage is null && pendingThumbnails.Add(rule))
                _ = LoadThumbnailAsync(rule);
        }

        private async Task LoadThumbnailAsync(RuleImagePreset rule)
        {
            try
            {
                var image = await Task.Run(() =>
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(rule.Path);
                    image.DecodePixelWidth = ThumbnailSize;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    image.EndInit();
                    image.Freeze();
                    return image;
                });

                if (rule.PreviewImage is null)
                {
                    rule.PreviewImage = image;
                    ThumbnailCache.Add(rule);
                }
            }
            catch
            {
            }
            finally
            {
                pendingThumbnails.Remove(rule);
            }
        }

        public void StartAnimation(RuleImagePreset rule)
        {
            StopAnimation();
            if (rule.PreviewImage is null || animation?.Target == rule)
                return;

            try
            {
                animation = new RuleImageAnimation(rule);
            }
            catch
            {
            }
        }

        public void StopAnimation()
        {
            animation?.Dispose();
            animation = null;
        }

        private void Edit(RuleImagePreset? rule)
        {
            var record = rule is null ? null : Records.FirstOrDefault(x => IsSamePath(x.Path, rule.Path));
            var title = record is null ? Texts.CreateRuleImage : Texts.EditRuleImage;
            if (RuleImageEditorViewModel.Show(title, record?.Name ?? Texts.NewRuleImage, record?.Parameters.Clone() ?? new()) is not { } result)
                return;

            StopAnimation();
            var path = record is not null && record.Name == result.Name ? record.Path : CreatePath(result.Name, record);
            if (path is null)
                return;

            try
            {
                RuleImageGenerator.Save(path, result.Parameters);
                if (record is not null && !IsSamePath(record.Path, path))
                    TryDelete(record.Path);
            }
            catch (Exception ex)
            {
                View.MessageWindow.Inform(Texts.RuleImageSaveFailed + Environment.NewLine + ex.Message);
                return;
            }

            if (record is null)
            {
                record = new RuleImageRecord();
                Records.Add(record);
            }

            record.Path = path;
            record.Name = result.Name;
            record.Parameters = result.Parameters;
            PresetStudioSettings.Default.Save();
            Refresh();
        }

        private static string? CreatePath(string name, RuleImageRecord? editing)
        {
            var fileName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            if (fileName.Length == 0)
                fileName = Texts.RuleImage;

            var path = Path.Combine(UserTransitionDirectory, fileName + ".png");
            if (!File.Exists(path) || (editing is not null && IsSamePath(editing.Path, path)))
                return path;

            if (Records.Any(record => IsSamePath(record.Path, path)))
                return PresetLibrary.Confirm(string.Format(Texts.ConfirmOverwrite, name)) ? path : null;

            for (var i = 2; ; i++)
            {
                var candidate = Path.Combine(UserTransitionDirectory, $"{fileName} ({i}).png");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        private void Delete(IReadOnlyList<RuleImagePreset> rules)
        {
            var targets = rules.Where(rule => rule.IsOwned).ToList();
            if (targets.Count == 0)
                return;

            var message = (targets.Count == 1 ? string.Format(Texts.ConfirmDelete, targets[0].Name) : string.Format(Texts.ConfirmDeleteRuleImages, targets.Count)) +
                Environment.NewLine + Texts.RuleImageDeleteNote;
            if (!PresetLibrary.Confirm(message))
                return;

            StopAnimation();
            foreach (var rule in targets)
            {
                TryDelete(rule.Path);
                foreach (var record in Records.Where(record => IsSamePath(record.Path, rule.Path)).ToList())
                    Records.Remove(record);
            }

            PresetStudioSettings.Default.Save();
            Refresh();
        }

        private void Apply(ITransitionEntry? entry)
        {
            if (getTimeline() is { } timeline && ApplyTo(entry, timeline, [.. getSelectedItems().OfType<IVideoItem>()], null))
                recordUndo();
        }

        public static bool CanDrop(ITransitionEntry entry, IItem? target) => target switch
        {
            null or TransitionItem => true,
            IVideoItem => entry is RuleImagePreset,
            _ => false,
        };

        public static bool Drop(ITransitionEntry entry, Timeline timeline, IItem? target, int frame, int layer) =>
            ApplyTo(entry, timeline, target is IVideoItem item ? [item] : [], (frame, layer));

        private static bool ApplyTo(ITransitionEntry? entry, Timeline timeline, IReadOnlyList<IVideoItem> items, (int Frame, int Layer)? placement)
        {
            var (plugin, file) = entry switch
            {
                TransitionPreset transition => (transition.Plugin, null),
                RuleImagePreset rule => (TransitionFactory.OrderdPlugins.FirstOrDefault(plugin => plugin.GetType().Name == "RuleImageTransitionPlugin"), rule.Path),
                _ => (null, (string?)null),
            };
            if (plugin is null)
                return false;

            var transitionItems = items.OfType<TransitionItem>().ToList();
            if (transitionItems.Count > 0)
            {
                foreach (var item in transitionItems)
                    SetTransition(item, plugin, file);
            }
            else if (file is not null && items.Count > 0)
            {
                var effectFactory = EffectFactories.VideoEffectFactories.FirstOrDefault(factory => factory.EffectType.Name == "InOutTransitionEffect");
                foreach (var item in items)
                {
                    if (effectFactory?.Create() is not { } effect)
                        continue;

                    SetFile(effect, file);
                    item.VideoEffects = item.VideoEffects.Add(effect);
                }
            }
            else if (!AddTransitionItem(timeline, plugin, file, placement))
            {
                return false;
            }

            return true;
        }

        private static void SetTransition(TransitionItem item, ITransitionPlugin plugin, string? file)
        {
            if (file is not null && item.TransitionType == plugin.GetType() && item.TransitionParameter is IFileItem current)
            {
                SetFile(current, file);
                return;
            }

            var parameter = plugin.CreateTransitionParameter();
            if (file is not null)
                SetFile(parameter, file);
            item.TransitionType = plugin.GetType();
            item.TransitionParameter = parameter;
        }

        private static bool AddTransitionItem(Timeline timeline, ITransitionPlugin plugin, string? file, (int Frame, int Layer)? placement)
        {
            var item = SettingsBase<ItemSettings>.Default.DefaultItems.TransitionItem.GetClone() as TransitionItem ?? new TransitionItem();
            SetTransition(item, plugin, file);

            if (placement is { } position)
                return timeline.TryAddItems([item], position.Frame, position.Layer);

            var frame = timeline.CurrentFrame;
            var layer = timeline.Items.Where(x => x.Frame <= frame && frame < x.Frame + x.Length).Select(x => x.Layer + 1).DefaultIfEmpty(0).Max();
            return timeline.TryAddItems([item], frame, layer);
        }

        private static void SetFile(object target, string path)
        {
            if (target is IFileItem fileItem)
                fileItem.ReplaceFile(fileItem.GetFiles().FirstOrDefault()!, path);
        }

        private void SelectSampleImage(bool isBefore)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = isBefore ? Texts.SelectBeforeImage : Texts.SelectAfterImage,
                Filter = Texts.ImageFileFilter,
            };
            if (dialog.ShowDialog(Application.Current?.MainWindow) != true)
                return;

            var settings = PresetStudioSettings.Default;
            SetSampleImages(
                isBefore ? dialog.FileName : settings.TransitionBeforeImagePath,
                isBefore ? settings.TransitionAfterImagePath : dialog.FileName);
        }

        private void SetSampleImages(string before, string after)
        {
            StopAnimation();
            var settings = PresetStudioSettings.Default;
            settings.TransitionBeforeImagePath = before;
            settings.TransitionAfterImagePath = after;
            settings.Save();
            SampleImagesChanged?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        }

        private static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(UserTransitionDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UserTransitionDirectory) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private static bool IsSamePath(string left, string right)
        {
            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        public void Dispose() => StopAnimation();
    }
}
