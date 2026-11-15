using System.Windows;
using System.Windows.Media;
using PresetStudio.Model;
using PresetStudio.View;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.ViewModel
{
    internal sealed class TemplateFieldOption(int itemIndex, TemplateFieldKind kind, string description, bool isUsed, string label) : Bindable
    {
        public int ItemIndex { get; } = itemIndex;

        public TemplateFieldKind Kind { get; } = kind;

        public string Description { get; } = description;

        private bool isUsed = isUsed;
        public bool IsUsed { get => isUsed; set => Set(ref isUsed, value); }

        private string label = label;
        public string Label { get => label; set => Set(ref label, value ?? string.Empty); }
    }

    internal sealed class TemplateFieldsViewModel
    {
        public IReadOnlyList<TemplateFieldOption> Options { get; }

        private TemplateFieldsViewModel(IReadOnlyList<TemplateFieldOption> options)
        {
            Options = options;
        }

        public static List<TemplateField>? Show(IReadOnlyList<TextItem> textItems, IReadOnlyList<TemplateField> current, Window? owner = null)
        {
            var options = new List<TemplateFieldOption>();
            for (var i = 0; i < textItems.Count; i++)
            {
                var preview = textItems[i].Text?.Split(["\r\n", "\n"], StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
                if (preview.Length > 16)
                    preview = preview[..16] + "…";

                foreach (var kind in Enum.GetValues<TemplateFieldKind>())
                {
                    var existing = current.FirstOrDefault(field => field.ItemIndex == i && field.Kind == kind);
                    var kindName = TemplateFieldAccessor.GetKindName(kind);
                    options.Add(new TemplateFieldOption(
                        i,
                        kind,
                        string.Format(Texts.TemplateFieldDescription, i + 1, preview, kindName),
                        existing is not null,
                        existing?.Label ?? (textItems.Count == 1 ? kindName : $"{kindName}{i + 1}")));
                }
            }

            var viewModel = new TemplateFieldsViewModel(options);
            var window = new TemplateFieldsWindow
            {
                DataContext = viewModel,
                Owner = owner ?? Application.Current?.MainWindow,
            };
            if (window.ShowDialog() != true)
                return null;

            return
            [
                .. options
                    .Where(option => option.IsUsed)
                    .Select(option => new TemplateField
                    {
                        ItemIndex = option.ItemIndex,
                        Kind = option.Kind,
                        Label = string.IsNullOrWhiteSpace(option.Label) ? TemplateFieldAccessor.GetKindName(option.Kind) : option.Label,
                    }),
            ];
        }
    }

    internal sealed class TemplateInputRow(TemplateField field, string text, Color color) : Bindable
    {
        public TemplateField Field { get; } = field;

        public string Label => Field.Label;

        public bool IsText => Field.Kind == TemplateFieldKind.Text;

        public bool IsColor => !IsText;

        private string text = text;
        public string Text { get => text; set => Set(ref text, value ?? string.Empty); }

        private Color color = color;
        public Color Color { get => color; set => Set(ref color, value); }
    }

    internal sealed class TemplateInputViewModel
    {
        public IReadOnlyList<TemplateInputRow> Rows { get; }

        private TemplateInputViewModel(IReadOnlyList<TemplateInputRow> rows)
        {
            Rows = rows;
        }

        public static bool Show(string title, IReadOnlyList<TextItem> textItems, IReadOnlyList<TemplateField> fields, Window? owner = null)
        {
            var rows = fields
                .Where(field => field.ItemIndex >= 0 && field.ItemIndex < textItems.Count)
                .Select(field => new TemplateInputRow(
                    field,
                    TemplateFieldAccessor.GetText(textItems[field.ItemIndex]),
                    TemplateFieldAccessor.GetColor(textItems[field.ItemIndex], field.Kind)))
                .ToList();
            if (rows.Count == 0)
                return true;

            var window = new TemplateInputWindow
            {
                DataContext = new TemplateInputViewModel(rows),
                Owner = owner ?? Application.Current?.MainWindow,
                Title = title,
            };
            if (window.ShowDialog() != true)
                return false;

            foreach (var row in rows)
            {
                var item = textItems[row.Field.ItemIndex];
                if (row.IsText)
                    TemplateFieldAccessor.SetText(item, row.Text);
                else
                    TemplateFieldAccessor.SetColor(item, row.Field.Kind, row.Color);
            }

            return true;
        }
    }
}
