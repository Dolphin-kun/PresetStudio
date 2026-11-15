using System.Windows;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using Font = YukkuriMovieMaker.Settings.Font;

namespace PresetStudio.Model
{
    internal enum FontKind
    {
        Other,
        Mincho,
        Gothic,
    }

    internal sealed class FontTagRecord
    {
        public string FontName { get; set; } = string.Empty;

        public string[] Tags { get; set; } = [];
    }

    internal sealed class FontEntry(Font font) : Bindable
    {
        public static string UncategorizedLabel => Texts.Uncategorized;

        private static readonly string[] gothicKeywords = ["ゴシック", "ｺﾞｼｯｸ", "gothic", "sans", "丸ゴ", "maru"];
        private static readonly string[] minchoKeywords = ["明朝", "mincho", "serif", "ming", "song", "教科書", "楷書"];

        private bool? isJapanese;

        public Font Font { get; } = font;

        public string Name => Font.FontName;

        public FontFamily Family { get; } = new FontFamily(string.IsNullOrEmpty(font.CanonicalFontName) ? font.FontName : font.CanonicalFontName);

        public FontWeight Weight { get; } = FontWeight.FromOpenTypeWeight(Math.Clamp(Convert.ToInt32(font.CanonicalFontWeight), 1, 999));

        public FontStyle Style { get; } = Convert.ToInt32(font.CanonicalFontStyle) switch
        {
            1 => FontStyles.Oblique,
            2 => FontStyles.Italic,
            _ => FontStyles.Normal,
        };

        public FontStretch Stretch { get; } = FontStretch.FromOpenTypeStretch(Math.Clamp(Convert.ToInt32(font.CanonicalFontStretch), 1, 9));

        public FontKind Kind { get; } = GetKind(font.FontName);

        public bool IsFavorite
        {
            get => Font.IsFavorite;
            set
            {
                if (Font.IsFavorite == value)
                    return;

                Font.IsFavorite = value;
                OnPropertyChanged(nameof(IsFavorite));
            }
        }

        public string Category
        {
            get => Font.GroupName ?? string.Empty;
            set
            {
                if (Category == value)
                    return;

                Font.GroupName = value;
                OnPropertyChanged(nameof(Category));
                OnPropertyChanged(nameof(CategoryLabel));
            }
        }

        public string CategoryLabel => Category.Length == 0 ? UncategorizedLabel : Category;

        private string[] tags = [];
        public string[] Tags
        {
            get => tags;
            set
            {
                if (!Set(ref tags, value ?? [], nameof(Tags)))
                    return;

                OnPropertyChanged(nameof(TagToolTip));
            }
        }

        public string? TagToolTip => tags.Length == 0 ? null : string.Join(" ", tags.Select(tag => "#" + tag));

        private bool isCurrent;
        public bool IsCurrent { get => isCurrent; set => Set(ref isCurrent, value); }

        public bool IsJapanese => isJapanese ??= HasJapaneseGlyph();

        private bool HasJapaneseGlyph()
        {
            try
            {
                return new Typeface(Family, Style, Weight, Stretch).TryGetGlyphTypeface(out var glyph) && glyph.CharacterToGlyphMap.ContainsKey('あ');
            }
            catch
            {
                return false;
            }
        }

        private static FontKind GetKind(string name)
        {
            var lower = name.ToLowerInvariant();
            if (gothicKeywords.Any(lower.Contains))
                return FontKind.Gothic;
            if (minchoKeywords.Any(lower.Contains))
                return FontKind.Mincho;
            return FontKind.Other;
        }
    }
}
