using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using PresetStudio.Settings;
using PresetStudio.View;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal static class PresetLibrary
    {
        private static ObservableCollection<TextStylePreset> Presets => PresetStudioSettings.Default.Presets;

        public static event EventHandler? FavoriteChanged;

        public static void NotifyFavoriteChanged()
        {
            PresetStudioSettings.Default.Save();
            FavoriteChanged?.Invoke(null, EventArgs.Empty);
        }

        public static HashSet<string> GetAllFolders()
        {
            var folders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in PresetStudioSettings.Default.PresetFolders.Concat(Presets.Select(preset => preset.Folder)))
            {
                for (var folder = NormalizeFolder(path); folder.Length > 0; folder = GetParentFolder(folder))
                    folders.Add(folder);
            }

            return folders;
        }

        public static string NormalizeFolder(string? path) =>
            string.Join("/", (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public static string GetParentFolder(string path) => path.LastIndexOf('/') is var index and >= 0 ? path[..index] : string.Empty;

        public static string GetFolderName(string path) => path[(path.LastIndexOf('/') + 1)..];

        public static string GetPreviewText(TextStylePreset preset) => preset.PreviewText ?? PresetStudioSettings.Default.PreviewText;

        public static string GetCategory(TextStylePreset preset) =>
            !string.IsNullOrWhiteSpace(preset.Category) || preset.Variants.Count > 0 ? preset.GroupName : string.Empty;

        public static List<(string Name, string Category)> GetNames() =>
            [.. Presets.SelectMany(preset => preset.Variants.Prepend(preset).Select(variant => (variant.Name, GetCategory(preset))))];

        public static List<string> GetCategories() =>
            [.. Presets.Select(GetCategory).Where(category => category.Length > 0).Distinct()];

        public static bool Confirm(string message, Window? owner = null) => MessageWindow.Confirm(message, owner);

        private static bool areIdsSaved;

        public static void EnsureIdsSaved()
        {
            if (areIdsSaved)
                return;

            areIdsSaved = true;
            PresetStudioSettings.Default.Save();
        }

        public static IEnumerable<TextStylePreset> GetAllPresets() => Presets.SelectMany(preset => preset.Variants.Prepend(preset));

        public static List<(TextStylePreset Group, TextStylePreset Target)> PutItems(
            string name,
            string category,
            string folder,
            IReadOnlyList<TextItem> items,
            string? previewText = null,
            Window? owner = null)
        {
            var placed = new List<(TextStylePreset Group, TextStylePreset Target)>();
            for (var i = 0; i < items.Count; i++)
            {
                var itemName = items.Count == 1 ? name : $"{name} {i + 1}";
                if (Put(itemName, category, folder, (TextItem)items[i].GetClone(), null, owner: owner) is { } result)
                {
                    result.Target.PreviewText = previewText;
                    placed.Add(result);
                }
            }

            return placed;
        }

        public static void Merge(IReadOnlyList<TextStylePreset> presets, string category)
        {
            var head = presets[0];
            var names = head.Variants.Prepend(head).Select(variant => variant.Name).ToHashSet();
            foreach (var other in presets.Skip(1))
            {
                foreach (var variant in other.Variants.Prepend(other).ToList())
                {
                    var name = variant.Name;
                    for (var i = 2; names.Contains(name); i++)
                        name = $"{variant.Name} ({i})";
                    names.Add(name);

                    head.Variants.Add(new TextStylePreset
                    {
                        Id = variant.Id,
                        Name = name,
                        Item = variant.Item,
                        Tags = variant.Tags,
                        PreviewText = variant.PreviewText,
                        PreviewImage = variant.PreviewImage,
                    });
                }

                Presets.Remove(other);
            }

            head.Category = category;
            head.Tags = [.. presets.SelectMany(preset => preset.Tags).Distinct()];
            head.IsFavorite = presets.Any(preset => preset.IsFavorite);
        }

        public static void Overwrite(TextStylePreset target, TextItem item)
        {
            target.Item = (TextItem)item.GetClone();
            target.PreviewImage = null;
            PresetStudioSettings.Default.Save();
        }

        public static (TextStylePreset Group, TextStylePreset Target) Duplicate(TextStylePreset group)
        {
            var source = group.Current;
            var category = GetCategory(group);
            var names = (category.Length > 0
                    ? group.Variants.Prepend(group)
                    : Presets.Where(preset => GetCategory(preset).Length == 0))
                .Select(preset => preset.Name)
                .ToHashSet();

            var name = string.Format(Texts.CopyName, source.Name);
            for (var i = 2; names.Contains(name); i++)
                name = string.Format(Texts.CopyNameNumbered, source.Name, i);

            var copy = new TextStylePreset
            {
                Name = name,
                Item = (TextItem)source.Item.GetClone(),
                Tags = source.Tags,
                PreviewText = source.PreviewText,
                PreviewImage = source.PreviewImage,
            };

            if (category.Length > 0)
            {
                group.Variants.Add(copy);
                return (group, copy);
            }

            copy.Folder = group.Folder;
            Presets.Insert(Presets.IndexOf(group) + 1, copy);
            return (copy, copy);
        }

        public static (TextStylePreset Group, TextStylePreset Target)? Put(
            string name,
            string category,
            string folder,
            TextItem item,
            BitmapSource? previewImage,
            TextStylePreset? excluded = null,
            Window? owner = null)
        {
            var group = string.IsNullOrEmpty(category)
                ? Presets.FirstOrDefault(preset => preset != excluded && string.IsNullOrWhiteSpace(preset.Category) && preset.Variants.Count == 0 && preset.Name == name)
                : Presets.FirstOrDefault(preset => GetCategory(preset) == category);

            if (group is null)
            {
                var preset = new TextStylePreset
                {
                    Name = name,
                    Category = string.IsNullOrEmpty(category) ? null : category,
                    Folder = folder,
                    Item = item,
                    PreviewImage = previewImage,
                };
                Presets.Add(preset);
                return (preset, preset);
            }

            if (group.Variants.Prepend(group).FirstOrDefault(variant => variant != excluded && variant.Name == name) is { } existing)
            {
                if (!Confirm(string.Format(Texts.ConfirmOverwrite, name), owner))
                    return null;

                existing.Item = item;
                existing.PreviewImage = previewImage;
                return (group, existing);
            }

            if (string.IsNullOrWhiteSpace(group.Category))
                group.Category = group.Name;

            var variant = new TextStylePreset
            {
                Name = name,
                Item = item,
                PreviewImage = previewImage,
            };
            group.Variants.Add(variant);
            return (group, variant);
        }
    }
}
