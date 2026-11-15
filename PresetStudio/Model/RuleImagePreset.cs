using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;

namespace PresetStudio.Model
{
    internal enum RuleImageSource
    {
        PresetStudio,
        User,
        Bundled,
    }

    internal sealed class RuleImagePreset(string path, string name, RuleImageSource source) : Bindable, ITransitionEntry
    {
        public string Path { get; } = path;

        public string Name { get; } = name;

        public RuleImageSource Source { get; } = source;

        public bool IsOwned => Source == RuleImageSource.PresetStudio;

        public string SourceLabel => Source switch
        {
            RuleImageSource.PresetStudio => "PresetStudio",
            RuleImageSource.User => Texts.User,
            _ => "YMM4",
        };

        private BitmapSource? previewImage;
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }
    }
}
