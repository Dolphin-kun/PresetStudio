using System.Runtime.CompilerServices;
using PresetStudio.Settings;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace PresetStudio.Model
{
    internal static class TextStyleApplier
    {
        private static readonly ConditionalWeakTable<object, HashSet<IVideoEffect>> appliedPresetEffects = [];

        public static TextStyleParts DefaultParts
        {
            get
            {
                var settings = PresetStudioSettings.Default;
                var parts = TextStyleParts.All;
                if (settings.KeepFont)
                    parts &= ~TextStyleParts.Font;
                if (settings.KeepFontSize)
                    parts &= ~TextStyleParts.FontSize;
                return parts;
            }
        }

        public static TextStyleParts StyleOnlyParts => TextStyleParts.All & ~TextStyleParts.Font & ~TextStyleParts.FontSize;

        public static TextStyleParts CurrentParts =>
            System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt) ? StyleOnlyParts : DefaultParts;

        public static void Apply(TextItem target, TextItem source) => Apply(target, source, DefaultParts);

        public static void Apply(TextItem target, TextStylePreset preset, TextStyleParts parts) => Apply(target, preset.Item, parts);

        public static void Replace(TextItem target, TextItem oldStyle, TextItem newStyle, TextStyleParts parts)
        {
            Apply(target, newStyle, parts & ~TextStyleParts.Effects);
            if (!parts.HasFlag(TextStyleParts.Effects))
                return;

            var effects = target.VideoEffects.ToList();
            foreach (var json in oldStyle.VideoEffects.Select(GetEffectJson))
            {
                var index = effects.FindIndex(effect => GetEffectJson(effect) == json);
                if (index >= 0)
                    effects.RemoveAt(index);
            }

            target.VideoEffects = [.. effects, .. ClonePresetEffects(newStyle)];
            appliedPresetEffects.Remove(target);
        }

        public static void ReplaceOnItem(IItem target, TextItem oldStyle, TextItem newStyle, TextStyleParts parts)
        {
            switch (target)
            {
                case TextItem text:
                    Replace(text, oldStyle, newStyle, parts);
                    break;
                case VoiceItem voice:
                    ApplyToVoice(voice, newStyle, parts & ~TextStyleParts.Effects);
                    if (parts.HasFlag(TextStyleParts.Effects))
                        voice.JimakuVideoEffects = SwapEffects(voice.JimakuVideoEffects, oldStyle, newStyle);
                    break;
                case IVideoItem video when parts.HasFlag(TextStyleParts.Effects):
                    video.VideoEffects = SwapEffects(video.VideoEffects, oldStyle, newStyle);
                    break;
            }

            appliedPresetEffects.Remove(target);
        }

        private static System.Collections.Immutable.ImmutableList<IVideoEffect> SwapEffects(IEnumerable<IVideoEffect> current, TextItem oldStyle, TextItem newStyle)
        {
            var effects = current.ToList();
            foreach (var json in oldStyle.VideoEffects.Select(GetEffectJson))
            {
                var index = effects.FindIndex(effect => GetEffectJson(effect) == json);
                if (index >= 0)
                    effects.RemoveAt(index);
            }

            return [.. effects, .. ClonePresetEffects(newStyle)];
        }

        public static bool HasSameStyle(TextItem item, TextItem style)
        {
            if (GetStyleSignature(item) != GetStyleSignature(style))
                return false;

            var effects = item.VideoEffects.Select(GetEffectJson).ToList();
            return style.VideoEffects.Select(GetEffectJson).All(effects.Remove);
        }

        private static string GetStyleSignature(TextItem item) => string.Join(
            "|",
            item.FontColor,
            item.Style,
            item.StyleColor,
            item.Bold,
            item.Italic,
            item.Underline,
            item.Strikethrough,
            YmmJson.GetJsonText(item.LineHeight2),
            YmmJson.GetJsonText(item.LetterSpacing2),
            item.WordWrap,
            YmmJson.GetJsonText(item.MaxWidth),
            item.BasePoint,
            item.IsTrimEndSpace,
            item.IsDevidedPerCharacter,
            item.DisplayInterval,
            item.DisplayDirection,
            item.HideInterval,
            item.HideDirection);

        private static string GetEffectJson(IVideoEffect effect) => YmmJson.GetJsonText<IVideoEffect>(effect);

        private static List<IVideoEffect> ClonePresetEffects(TextItem source) =>
        [
            .. source.VideoEffects
                .Select(effect => YmmJson.GetClone(effect))
                .OfType<IVideoEffect>(),
        ];

        public static void Apply(TextItem target, TextItem source, TextStyleParts parts)
        {
            if (parts.HasFlag(TextStyleParts.Font))
                target.Font = source.Font;

            if (parts.HasFlag(TextStyleParts.FontSize))
                target.FontSize.CopyFrom(source.FontSize);

            if (parts.HasFlag(TextStyleParts.Color))
                target.FontColor = source.FontColor;

            if (parts.HasFlag(TextStyleParts.Decoration))
            {
                target.Style = source.Style;
                target.StyleColor = source.StyleColor;
            }

            if (parts.HasFlag(TextStyleParts.Format))
            {
                target.Bold = source.Bold;
                target.Italic = source.Italic;
                target.Underline = source.Underline;
                target.Strikethrough = source.Strikethrough;
            }

            if (parts.HasFlag(TextStyleParts.Layout))
            {
                target.LineHeight2.CopyFrom(source.LineHeight2);
                target.LetterSpacing2.CopyFrom(source.LetterSpacing2);
                target.WordWrap = source.WordWrap;
                target.MaxWidth.CopyFrom(source.MaxWidth);
                target.BasePoint = source.BasePoint;
                target.IsTrimEndSpace = source.IsTrimEndSpace;
            }

            if (parts.HasFlag(TextStyleParts.Animation))
            {
                target.IsDevidedPerCharacter = source.IsDevidedPerCharacter;
                target.DisplayInterval = source.DisplayInterval;
                target.DisplayDirection = source.DisplayDirection;
                target.HideInterval = source.HideInterval;
                target.HideDirection = source.HideDirection;
            }

            if (parts.HasFlag(TextStyleParts.Effects))
                target.VideoEffects = ReplacePresetEffects(target, target.VideoEffects, source);
        }

        public static bool CanApply(IItem target, TextItem source, TextStyleParts parts) => target switch
        {
            TextItem or VoiceItem => true,
            IVideoItem => parts.HasFlag(TextStyleParts.Effects) && source.VideoEffects.Count > 0,
            _ => false,
        };

        public static bool ApplyToItem(IItem target, TextItem source, TextStyleParts parts)
        {
            switch (target)
            {
                case TextItem text:
                    Apply(text, source, parts);
                    return true;
                case VoiceItem voice:
                    ApplyToVoice(voice, source, parts);
                    return true;
                case IVideoItem video when CanApply(target, source, parts):
                    video.VideoEffects = ReplacePresetEffects(video, video.VideoEffects, source);
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyToVoice(VoiceItem target, TextItem source, TextStyleParts parts)
        {
            if (parts.HasFlag(TextStyleParts.Font))
                target.Font = source.Font;

            if (parts.HasFlag(TextStyleParts.FontSize))
                target.FontSize.CopyFrom(source.FontSize);

            if (parts.HasFlag(TextStyleParts.Color))
                target.FontColor = source.FontColor;

            if (parts.HasFlag(TextStyleParts.Decoration))
            {
                target.Style = source.Style;
                target.StyleColor = source.StyleColor;
            }

            if (parts.HasFlag(TextStyleParts.Format))
            {
                target.Bold = source.Bold;
                target.Italic = source.Italic;
                target.Underline = source.Underline;
                target.Strikethrough = source.Strikethrough;
            }

            if (parts.HasFlag(TextStyleParts.Layout))
            {
                target.LineHeight2.CopyFrom(source.LineHeight2);
                target.LetterSpacing2.CopyFrom(source.LetterSpacing2);
                target.WordWrap = source.WordWrap;
                target.MaxWidth.CopyFrom(source.MaxWidth);
                target.BasePoint = source.BasePoint;
                target.IsTrimEndSpace = source.IsTrimEndSpace;
            }

            if (parts.HasFlag(TextStyleParts.Animation))
            {
                target.IsDevidedPerCharacter = source.IsDevidedPerCharacter;
                target.DisplayInterval = source.DisplayInterval;
                target.DisplayDirection = source.DisplayDirection;
                target.HideInterval = source.HideInterval;
                target.HideDirection = source.HideDirection;
            }

            if (parts.HasFlag(TextStyleParts.Effects))
                target.JimakuVideoEffects = ReplacePresetEffects(target, target.JimakuVideoEffects, source);
        }

        private static System.Collections.Immutable.ImmutableList<IVideoEffect> ReplacePresetEffects(object owner, IEnumerable<IVideoEffect> current, TextItem source)
        {
            var presetEffects = ClonePresetEffects(source);
            var appliedEffects = appliedPresetEffects.GetValue(owner, _ => new HashSet<IVideoEffect>(ReferenceEqualityComparer.Instance));
            var result = System.Collections.Immutable.ImmutableList.CreateRange([.. current.Where(effect => !appliedEffects.Contains(effect)), .. presetEffects]);
            appliedEffects.UnionWith(presetEffects);
            return result;
        }
    }
}
