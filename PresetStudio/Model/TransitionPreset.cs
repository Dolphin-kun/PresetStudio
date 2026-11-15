using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Transition;

namespace PresetStudio.Model
{
    internal interface ITransitionEntry : IPreviewItem
    {
        string Name { get; }

        string SourceLabel { get; }
    }

    internal sealed class TransitionPreset(ITransitionPlugin plugin) : Bindable, ITransitionEntry
    {
        public ITransitionPlugin Plugin { get; } = plugin;

        public string Name => Plugin.Name;

        public string SourceLabel => string.IsNullOrWhiteSpace(Plugin.DefaultGroupName) ? Texts.Transitions : string.Format(Texts.TransitionGroup, Plugin.DefaultGroupName);

        private BitmapSource? previewImage;
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }
    }
}
