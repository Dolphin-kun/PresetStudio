using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Settings;

namespace PresetStudio.Model
{
    internal sealed class AudioEffectPreset : Bindable
    {
        public EffectTemplate<IAudioEffect> Template { get; }

        public EffectSource Source { get; }

        public bool IsUserTemplate => Source == EffectSource.User;

        public string SourceLabel { get; }

        public string Name => string.IsNullOrWhiteSpace(Template.Name) ? Texts.Unnamed : Template.Name;

        public string EffectListText => Template.Effects.Count == 0
            ? Texts.NoEffects
            : string.Join(" / ", Template.Effects.Select(effect => effect.Label));

        public bool HasEffectList => Source != EffectSource.Default;

        private IReadOnlyList<double>? peaks;
        public IReadOnlyList<double>? Peaks { get => peaks; set => Set(ref peaks, value); }

        private bool isPlaying;
        public bool IsPlaying { get => isPlaying; set => Set(ref isPlaying, value); }

        public AudioEffectPreset(EffectTemplate<IAudioEffect> template, EffectSource source, string sourceLabel)
        {
            Template = template;
            Source = source;
            SourceLabel = sourceLabel;

            if (source != EffectSource.Default)
                template.Effects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EffectListText));
        }
    }
}
