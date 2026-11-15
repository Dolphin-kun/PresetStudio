using System.Collections.ObjectModel;
using System.Windows;
using PresetStudio.View;
using YukkuriMovieMaker.Commons;

namespace PresetStudio.ViewModel
{
    internal sealed class TagInputViewModel : Bindable
    {
        private static readonly char[] separators = [' ', '　', ',', '、'];

        private readonly IReadOnlyList<string> allTags;

        public string PlaceHolder { get; } = Texts.TagsPlaceholder;

        private string text;
        public string Text { get => text; set => Set(ref text, value ?? string.Empty); }

        public ObservableCollection<string> Suggestions { get; } = [];

        private bool isSuggestionOpen;
        public bool IsSuggestionOpen { get => isSuggestionOpen; set => Set(ref isSuggestionOpen, value); }

        private TagInputViewModel(string initialText, IReadOnlyList<string> allTags)
        {
            text = initialText;
            this.allTags = allTags;
        }

        public static string[] ParseTags(string text) =>
        [
            .. text
                .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.TrimStart('#'))
                .Where(tag => tag.Length > 0)
                .Distinct(),
        ];

        public void UpdateSuggestions(int caretIndex, bool showAll = false)
        {
            var (start, end) = GetTokenRange(caretIndex);
            var prefix = text[start..Math.Clamp(caretIndex, start, text.Length)].TrimStart('#');
            var used = ParseTags(text[..start] + " " + text[end..]).ToHashSet();

            Suggestions.Clear();
            if (prefix.Length > 0 || showAll)
            {
                var matches = allTags
                    .Where(tag => !used.Contains(tag) && tag != prefix && tag.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(tag => !tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                foreach (var tag in matches)
                    Suggestions.Add(tag);
            }

            IsSuggestionOpen = Suggestions.Count > 0;
        }

        public int Accept(string tag, int caretIndex)
        {
            var (start, end) = GetTokenRange(caretIndex);
            var head = text[..start] + tag + " ";
            Text = head + text[end..].TrimStart(separators);
            IsSuggestionOpen = false;
            return head.Length;
        }

        private (int Start, int End) GetTokenRange(int caretIndex)
        {
            var caret = Math.Clamp(caretIndex, 0, text.Length);
            var start = caret == 0 ? 0 : text.LastIndexOfAny(separators, caret - 1) + 1;
            var end = text.IndexOfAny(separators, caret);
            return (start, end < 0 ? text.Length : end);
        }

        public static string? Show(string title, string initialText, IReadOnlyList<string> allTags, Window? owner = null)
        {
            var viewModel = new TagInputViewModel(initialText, allTags);
            var window = new TagInputWindow(viewModel)
            {
                Owner = owner ?? Application.Current?.MainWindow,
                Title = title,
            };

            return window.ShowDialog() == true ? viewModel.Text : null;
        }
    }
}
