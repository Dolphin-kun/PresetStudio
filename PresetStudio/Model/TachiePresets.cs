using System.Collections.Immutable;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal sealed class FacePreset : Bindable, IPreviewItem
    {
        private string name = string.Empty;
        public string Name { get => name; set => Set(ref name, value ?? string.Empty); }

        public string CharacterName { get; set; } = string.Empty;

        public ITachieFaceParameter? FaceParameter { get; set; }

        public ImmutableList<IVideoEffect> FaceEffects { get; set; } = [];

        [JsonIgnore]
        public bool IsDefault { get; init; }

        [JsonIgnore]
        public string GroupLabel { get; } = Texts.FaceGroup;

        private BitmapSource? previewImage;
        [JsonIgnore]
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }
    }

    internal sealed class TachieItemPreset : Bindable, IPreviewItem
    {
        private string name = string.Empty;
        public string Name { get => name; set => Set(ref name, value ?? string.Empty); }

        public string CharacterName { get; set; } = string.Empty;

        public TachieItem Item { get; set; } = new();

        [JsonIgnore]
        public string GroupLabel { get; } = Texts.TachieItemGroup;

        private BitmapSource? previewImage;
        [JsonIgnore]
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }
    }
}
