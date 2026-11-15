using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Settings;

namespace PresetStudio.Model
{
    internal sealed class EffectPreset : Bindable, IPreviewItem
    {
        public EffectTemplate<IVideoEffect> Template { get; }

        public EffectSource Source { get; }

        public bool IsUserTemplate => Source == EffectSource.User;

        public string SourceLabel { get; }

        public string Name => string.IsNullOrWhiteSpace(Template.Name) ? Texts.Unnamed : Template.Name;

        public string? EffectListText => Source == EffectSource.Default
            ? null
            : Template.Effects.Count == 0
                ? Texts.NoEffects
                : string.Join(Environment.NewLine, Template.Effects.Select(effect => effect.Label));

        private BitmapSource? previewImage;
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }

        public EffectPreset(EffectTemplate<IVideoEffect> template, EffectSource source, string sourceLabel)
        {
            Template = template;
            Source = source;
            SourceLabel = sourceLabel;

            if (source != EffectSource.Default)
                template.Effects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EffectListText));
        }
    }
}
