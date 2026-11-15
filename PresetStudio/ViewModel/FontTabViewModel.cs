using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using PresetStudio.Model;
using PresetStudio.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace PresetStudio.ViewModel
{
    internal sealed class FontTabViewModel : Bindable
    {
        private static readonly NaturalComparer nameComparer = new();

        private readonly Func<IReadOnlyList<IItem>> getSelectedItems;
        private readonly Action recordUndo;
        private bool isJapaneseChecked;
        private IReadOnlyList<FontEntry> selectedEntries = [];

        public ActionCommand EditCategoryCommand { get; }

        public ActionCommand EditTagsCommand { get; }

        public ObservableCollection<FontEntry> Entries { get; } = [];

        private string searchText = string.Empty;
        public string SearchText { get => searchText; set => SetFilter(ref searchText, value ?? string.Empty, nameof(SearchText)); }

        private bool isFavoriteOnly;
        public bool IsFavoriteOnly { get => isFavoriteOnly; set => SetFilter(ref isFavoriteOnly, value, nameof(IsFavoriteOnly)); }

        private bool isJapaneseOnly;
        public bool IsJapaneseOnly
        {
            get => isJapaneseOnly;
            set
            {
                if (value && !isJapaneseChecked)
                    _ = PrepareJapaneseAsync();
                SetFilter(ref isJapaneseOnly, value, nameof(IsJapaneseOnly));
            }
        }

        private bool isMinchoOnly;
        public bool IsMinchoOnly { get => isMinchoOnly; set => SetFilter(ref isMinchoOnly, value, nameof(IsMinchoOnly)); }

        private bool isGothicOnly;
        public bool IsGothicOnly { get => isGothicOnly; set => SetFilter(ref isGothicOnly, value, nameof(IsGothicOnly)); }

        public string SampleText
        {
            get => PresetStudioSettings.Default.FontSampleText;
            set
            {
                if (PresetStudioSettings.Default.FontSampleText == value)
                    return;

                PresetStudioSettings.Default.FontSampleText = value;
                OnPropertyChanged(nameof(SampleText));
            }
        }

        private bool isPreparingJapanese;
        public bool IsPreparingJapanese { get => isPreparingJapanese; private set => Set(ref isPreparingJapanese, value); }

        public FontTabViewModel(Func<IReadOnlyList<IItem>> getSelectedItems, Action recordUndo)
        {
            this.getSelectedItems = getSelectedItems;
            this.recordUndo = recordUndo;
            var view = CollectionViewSource.GetDefaultView(Entries);
            view.Filter = item => item is FontEntry entry && Matches(entry);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FontEntry.CategoryLabel)));

            EditCategoryCommand = new ActionCommand(
                parameter => GetTargets(parameter).Count > 0,
                parameter => EditCategory(GetTargets(parameter)));
            EditTagsCommand = new ActionCommand(
                parameter => GetTargets(parameter).Count > 0,
                parameter => EditTags(GetTargets(parameter)));
        }

        public void SetSelected(IEnumerable<object> items)
        {
            selectedEntries = [.. items.OfType<FontEntry>()];
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private List<FontEntry> GetTargets(object? parameter)
        {
            if (parameter is FontEntry target)
                return selectedEntries.Contains(target) ? [.. selectedEntries] : [target];

            return [.. selectedEntries];
        }

        private void EditCategory(List<FontEntry> targets)
        {
            var categories = Entries.Select(entry => entry.Category).Where(category => category.Length > 0).Distinct().Order(nameComparer).Select(category => (category, string.Empty));
            var initial = targets.Select(entry => entry.Category).Distinct().Count() == 1 ? targets[0].Category : string.Empty;
            if (NameInputViewModel.Show(Texts.SetCategory, Texts.CategoryClearable, categories, initialName: initial, allowEmpty: true) is not { Name: var category })
                return;

            foreach (var entry in targets)
                entry.Category = category;

            SettingsBase<FontSettings>.Default.Save();
            Resort();
        }

        private void EditTags(List<FontEntry> targets)
        {
            var allTags = PresetStudioSettings.Default.FontTags.SelectMany(record => record.Tags).Distinct().Order(nameComparer).ToList();
            var initial = targets.Select(entry => string.Join(" ", entry.Tags)).Distinct().Count() == 1 ? string.Join(" ", targets[0].Tags) : string.Empty;
            if (TagInputViewModel.Show(Texts.EditTags, initial, allTags) is not { } text)
                return;

            var tags = TagInputViewModel.ParseTags(text);
            var records = PresetStudioSettings.Default.FontTags;
            foreach (var entry in targets)
            {
                entry.Tags = tags;
                var record = records.FirstOrDefault(x => x.FontName == entry.Name);
                if (tags.Length == 0)
                {
                    if (record is not null)
                        records.Remove(record);
                }
                else if (record is null)
                {
                    records.Add(new FontTagRecord { FontName = entry.Name, Tags = tags });
                }
                else
                {
                    record.Tags = tags;
                }
            }

            PresetStudioSettings.Default.Save();
            CollectionViewSource.GetDefaultView(Entries).Refresh();
        }

        private void SetFilter<T>(ref T field, T value, string propertyName)
        {
            if (Set(ref field, value, propertyName))
                CollectionViewSource.GetDefaultView(Entries).Refresh();
        }

        private bool Matches(FontEntry entry)
        {
            if (isFavoriteOnly && !entry.IsFavorite)
                return false;

            if (isMinchoOnly || isGothicOnly)
            {
                if (!(isMinchoOnly && entry.Kind == FontKind.Mincho) && !(isGothicOnly && entry.Kind == FontKind.Gothic))
                    return false;
            }

            if (isJapaneseOnly && isJapaneseChecked && !entry.IsJapanese)
                return false;

            return searchText
                .Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries)
                .All(token => token.StartsWith('#')
                    ? token.Length == 1 || entry.Tags.Any(tag => tag.Contains(token[1..], StringComparison.OrdinalIgnoreCase))
                    : entry.Name.Contains(token, StringComparison.OrdinalIgnoreCase) || entry.Category.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        public void Refresh()
        {
            var settings = SettingsBase<FontSettings>.Default;
            var fonts = settings.SystemFonts
                .Concat(settings.CustomFonts)
                .Where(font => font.IsEnabled)
                .ToList();

            if (fonts.Count == Entries.Count && Entries.Select(entry => entry.Font).ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(fonts))
            {
                UpdateCurrent();
                return;
            }

            var tagMap = PresetStudioSettings.Default.FontTags
                .GroupBy(record => record.FontName)
                .ToDictionary(group => group.Key, group => group.First().Tags);

            Entries.Clear();
            foreach (var entry in Sort(fonts.Select(font => new FontEntry(font) { Tags = tagMap.GetValueOrDefault(font.FontName) ?? [] })))
                Entries.Add(entry);
            UpdateCurrent();
        }

        public void Resort()
        {
            var sorted = Sort(Entries).ToList();
            Entries.Clear();
            foreach (var entry in sorted)
                Entries.Add(entry);
        }

        private static IEnumerable<FontEntry> Sort(IEnumerable<FontEntry> entries) => entries
            .OrderBy(entry => entry.Category.Length == 0)
            .ThenBy(entry => entry.Category, nameComparer)
            .ThenByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.Name, nameComparer);

        public void SaveFavorites()
        {
            SettingsBase<FontSettings>.Default.Save();
            Resort();
        }

        public void UpdateCurrent()
        {
            var current = getSelectedItems()
                .Select(item => item switch
                {
                    TextItem text => text.Font,
                    VoiceItem voice => voice.Font,
                    _ => null,
                })
                .FirstOrDefault(font => font is not null);

            foreach (var entry in Entries)
                entry.IsCurrent = entry.Name == current;
        }

        public void Apply(FontEntry entry)
        {
            var applied = false;
            foreach (var item in getSelectedItems())
            {
                switch (item)
                {
                    case TextItem text:
                        text.Font = entry.Name;
                        applied = true;
                        break;
                    case VoiceItem voice:
                        voice.Font = entry.Name;
                        applied = true;
                        break;
                }
            }

            if (!applied)
                return;

            recordUndo();
            UpdateCurrent();
        }

        private async Task PrepareJapaneseAsync()
        {
            if (isJapaneseChecked || isPreparingJapanese)
                return;

            IsPreparingJapanese = true;
            var dispatcher = Dispatcher.CurrentDispatcher;
            var entries = Entries.ToList();
            await Task.Run(() =>
            {
                foreach (var entry in entries)
                    _ = entry.IsJapanese;
            });
            await dispatcher.InvokeAsync(() =>
            {
                isJapaneseChecked = true;
                CollectionViewSource.GetDefaultView(Entries).Refresh();
                IsPreparingJapanese = false;
            });
        }
    }
}
