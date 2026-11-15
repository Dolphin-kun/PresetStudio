using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal sealed class TextStylePreset : Bindable, IPreviewItem
    {
        private string id = Guid.NewGuid().ToString("N");
        public string Id { get => id; set => id = string.IsNullOrEmpty(value) ? Guid.NewGuid().ToString("N") : value; }

        private bool isLinked;
        [JsonIgnore]
        public bool IsLinked { get => isLinked; set => Set(ref isLinked, value); }

        private string name = Texts.NewPreset;
        public string Name { get => name; set => Set(ref name, value, nameof(Name), nameof(GroupName), nameof(DisplayName), nameof(ToolTipText), nameof(NameToolTipText)); }

        private string? category;
        public string? Category { get => category; set => Set(ref category, value, nameof(Category), nameof(GroupName), nameof(DisplayName)); }

        [JsonIgnore]
        public string GroupName => string.IsNullOrWhiteSpace(Category) ? Name : Category;

        private string folder = string.Empty;
        public string Folder { get => folder; set => Set(ref folder, value ?? string.Empty, nameof(Folder), nameof(ToolTipText)); }

        private string[] tags = [];
        public string[] Tags { get => tags; set => Set(ref tags, value ?? [], nameof(Tags), nameof(TagText), nameof(HasTags)); }

        [JsonIgnore]
        public string TagText => string.Join(" ", Tags.Select(tag => "#" + tag));

        [JsonIgnore]
        public bool HasTags => Tags.Length > 0;

        public TextItem Item { get; set; } = new();

        public ObservableCollection<TextStylePreset> Variants { get; } = [];

        private int currentIndex;
        [JsonIgnore]
        public int CurrentIndex { get => currentIndex; private set => Set(ref currentIndex, value, nameof(CurrentIndex), nameof(Current), nameof(ToolTipText)); }

        [JsonIgnore]
        public TextStylePreset Current => currentIndex <= 0 || currentIndex > Variants.Count ? this : Variants[currentIndex - 1];

        [JsonIgnore]
        public bool IsGroup => Variants.Count > 0 || !string.IsNullOrWhiteSpace(Category);

        [JsonIgnore]
        public string DisplayName => IsGroup ? $"{GroupName} ({Variants.Count + 1})" : Name;

        [JsonIgnore]
        public string? ToolTipText
        {
            get
            {
                var lines = new List<string>();
                if (IsGroup)
                    lines.AddRange(Variants.Prepend(this).Select(variant => (variant == Current ? "▶ " : "　 ") + variant.Name));

                if (Folder.Length > 0)
                    lines.Add(string.Format(Texts.FolderLabel, Folder));

                return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
            }
        }

        private string? previewText;
        public string? PreviewText { get => previewText; set => Set(ref previewText, string.IsNullOrEmpty(value) ? null : value); }

        private bool isFavorite;
        public bool IsFavorite { get => isFavorite; set => Set(ref isFavorite, value); }

        [JsonIgnore]
        public string NameToolTipText => ToolTipText is { } text ? DisplayName + Environment.NewLine + text : DisplayName;

        private BitmapSource? previewImage;
        [JsonIgnore]
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }

        public TextStylePreset()
        {
            Variants.CollectionChanged += (_, _) =>
            {
                CurrentIndex = Math.Min(currentIndex, Variants.Count);
                OnPropertyChanged(nameof(Current));
                RefreshDisplayName();
            };
        }

        public void MoveVariant(int delta)
        {
            var count = Variants.Count + 1;
            CurrentIndex = ((currentIndex + delta) % count + count) % count;
        }

        public bool CanMoveCurrent(int delta) => Variants.Count > 0 && currentIndex + delta >= 0 && currentIndex + delta <= Variants.Count;

        public void MoveCurrent(int delta)
        {
            if (!CanMoveCurrent(delta))
                return;

            if (string.IsNullOrWhiteSpace(Category))
                Category = Name;

            var from = GetNode(currentIndex);
            var to = GetNode(currentIndex + delta);
            (from.Id, to.Id) = (to.Id, from.Id);
            (from.Name, to.Name) = (to.Name, from.Name);
            (from.Item, to.Item) = (to.Item, from.Item);
            (from.PreviewImage, to.PreviewImage) = (to.PreviewImage, from.PreviewImage);
            (from.PreviewText, to.PreviewText) = (to.PreviewText, from.PreviewText);

            CurrentIndex += delta;
            RefreshDisplayName();
        }

        private TextStylePreset GetNode(int index) => index == 0 ? this : Variants[index - 1];

        public void ShowVariant(TextStylePreset variant) => CurrentIndex = variant == this ? 0 : Variants.IndexOf(variant) + 1;

        public void RefreshDisplayName()
        {
            OnPropertyChanged(nameof(IsGroup));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ToolTipText));
            OnPropertyChanged(nameof(NameToolTipText));
        }

        public override string ToString() => Name;
    }
}
