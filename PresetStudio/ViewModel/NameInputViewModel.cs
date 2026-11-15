using System.Windows;
using PresetStudio.View;
using YukkuriMovieMaker.Commons;

namespace PresetStudio.ViewModel
{
    internal sealed class NameInputViewModel : Bindable
    {
        private readonly Dictionary<string, string> nameCategories = [];
        private bool isCategoryEditedByUser;

        public string PlaceHolder { get; }

        public string CategoryPlaceHolder { get; } = Texts.Category;

        public bool IsCategoryEditable { get; }

        public IReadOnlyList<string> Names { get; }

        public IReadOnlyList<string> Categories { get; }

        public bool HasNameSuggestions => Names.Count > 0;

        public bool UsesNameTextBox => Names.Count == 0;

        public bool HasCategorySuggestions => Categories.Count > 0;

        public bool UsesCategoryTextBox => Categories.Count == 0;

        private string name;
        public string Name
        {
            get => name;
            set
            {
                if (!Set(ref name, value ?? string.Empty, nameof(Name), nameof(CanSubmit)))
                    return;

                if (IsCategoryEditable && !isCategoryEditedByUser && nameCategories.TryGetValue(name, out var nameCategory))
                {
                    category = nameCategory;
                    OnPropertyChanged(nameof(Category));
                }
            }
        }

        private string category;
        public string Category
        {
            get => category;
            set
            {
                if (Set(ref category, value ?? string.Empty))
                    isCategoryEditedByUser = true;
            }
        }

        public bool AllowEmpty { get; }

        public bool IsPreviewTextEditable { get; }

        public string PreviewTextPlaceHolder { get; } = string.Format(Texts.PreviewTextDefault, PresetStudio.Settings.PresetStudioSettings.Default.PreviewText);

        private string previewText = string.Empty;
        public string PreviewText { get => previewText; set => Set(ref previewText, value ?? string.Empty); }

        public bool CanSubmit => AllowEmpty || !string.IsNullOrWhiteSpace(Name);

        private NameInputViewModel(
            string placeHolder,
            IEnumerable<(string Name, string Category)> names,
            IReadOnlyList<string>? categories,
            string initialName,
            string initialCategory,
            bool allowEmpty,
            string? initialPreviewText)
        {
            IsPreviewTextEditable = initialPreviewText is not null;
            previewText = initialPreviewText ?? string.Empty;
            PlaceHolder = placeHolder;
            AllowEmpty = allowEmpty;
            IsCategoryEditable = categories is not null;
            Categories = categories ?? [];
            name = initialName;
            category = initialCategory;
            isCategoryEditedByUser = !string.IsNullOrEmpty(initialCategory) || !string.IsNullOrEmpty(initialName);

            var list = names.ToList();
            Names = [.. list.Select(x => x.Name).Distinct()];
            foreach (var (itemName, itemCategory) in list)
                nameCategories[itemName] = itemCategory;
        }

        public static NameInputResult? Show(
            string title,
            string placeHolder,
            IEnumerable<(string Name, string Category)> names,
            IReadOnlyList<string>? categories = null,
            string initialName = "",
            string initialCategory = "",
            bool allowEmpty = false,
            Window? owner = null,
            string? previewText = null)
        {
            var viewModel = new NameInputViewModel(placeHolder, names, categories, initialName, initialCategory, allowEmpty, previewText);
            var window = new NameInputWindow(viewModel)
            {
                Owner = owner ?? Application.Current?.MainWindow,
                Title = title,
            };

            return window.ShowDialog() == true
                ? new NameInputResult(viewModel.Name.Trim(), viewModel.Category.Trim(), viewModel.PreviewText.Length == 0 ? null : viewModel.PreviewText)
                : null;
        }
    }

    internal sealed record NameInputResult(string Name, string Category, string? PreviewText);
}
