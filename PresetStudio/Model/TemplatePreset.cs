using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Settings;

namespace PresetStudio.Model
{
    internal sealed class TemplatePreset(ItemTemplate template) : Bindable, IPreviewItem
    {
        public ItemTemplate Template { get; } = template;

        public string Name => string.IsNullOrWhiteSpace(Template.Name) ? Texts.Unnamed : Template.Name;

        public string Key => $"{Template.Group}/{Template.Name}";

        private bool isFullCanvas;
        public bool IsFullCanvas { get => isFullCanvas; set => Set(ref isFullCanvas, value); }

        private int fieldCount;
        public int FieldCount { get => fieldCount; set => Set(ref fieldCount, value, nameof(FieldCount), nameof(HasFields)); }

        public bool HasFields => fieldCount > 0;

        private BitmapSource? previewImage;
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }
    }
}
