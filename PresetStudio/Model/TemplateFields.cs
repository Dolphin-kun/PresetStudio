using System.Windows.Media;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal enum TemplateFieldKind
    {
        Text,
        FontColor,
        StyleColor,
    }

    internal sealed class TemplateField
    {
        public int ItemIndex { get; set; }

        public TemplateFieldKind Kind { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    internal sealed class TemplateFieldSet
    {
        public string Key { get; set; } = string.Empty;

        public List<TemplateField> Fields { get; set; } = [];
    }

    internal static class TemplateFieldAccessor
    {
        public static string GetKindName(TemplateFieldKind kind) => kind switch
        {
            TemplateFieldKind.FontColor => Texts.FontColor,
            TemplateFieldKind.StyleColor => Texts.StyleColor,
            _ => Texts.Text,
        };

        public static string GetText(TextItem item) => item.Text ?? string.Empty;

        public static Color GetColor(TextItem item, TemplateFieldKind kind) => kind == TemplateFieldKind.StyleColor ? item.StyleColor : item.FontColor;

        public static void SetText(TextItem item, string text) => item.Text = text;

        public static void SetColor(TextItem item, TemplateFieldKind kind, Color color)
        {
            if (kind == TemplateFieldKind.StyleColor)
                item.StyleColor = color;
            else
                item.FontColor = color;
        }
    }
}
