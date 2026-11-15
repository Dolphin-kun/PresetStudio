using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PresetStudio.ViewModel;

namespace PresetStudio.View
{
    internal partial class TextSplitWindow : Window
    {
        private readonly TextSplitViewModel viewModel;
        private readonly List<int> lineStarts = [];

        public TextSplitWindow(TextSplitViewModel viewModel)
        {
            this.viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;
            viewModel.SegmentsChanged += Rebuild;
            Closed += (_, _) => viewModel.SegmentsChanged -= Rebuild;
            Rebuild();
            Loaded += (_, _) => SourceText.Focus();
        }

        private void Rebuild()
        {
            var (start, end) = GetSelection();
            var text = viewModel.Text;
            var cuts = viewModel.Segments.Skip(1).Select(segment => segment[0]).ToHashSet();
            var markerBrush = (Brush)(TryFindResource(SystemColors.HighlightBrushKey) ?? Brushes.DodgerBlue);
            var document = new FlowDocument { PagePadding = new Thickness(2) };
            lineStarts.Clear();

            var lineStart = 0;
            for (var index = 0; index <= text.Length; index++)
            {
                if (index < text.Length && text[index] is not ('\r' or '\n'))
                    continue;

                lineStarts.Add(lineStart);
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                var runStart = lineStart;
                for (var position = lineStart; position <= index; position++)
                {
                    if (position < index && !(cuts.Contains(position) && position > lineStart))
                        continue;

                    if (position > runStart)
                        paragraph.Inlines.Add(new Run(text[runStart..position]));

                    if (position < index)
                        paragraph.Inlines.Add(CreateMarker(markerBrush));
                    runStart = position;
                }

                document.Blocks.Add(paragraph);
                if (index < text.Length && text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                lineStart = index + 1;
            }

            SourceText.Document = document;
            SetSelection(start, end);
        }

        private InlineUIContainer CreateMarker(Brush brush) => new(new System.Windows.Shapes.Rectangle
        {
            Width = 2,
            Height = SourceText.FontSize * 1.2,
            Margin = new Thickness(2, 0, 2, 0),
            Fill = brush,
        })
        {
            BaselineAlignment = BaselineAlignment.Center,
        };

        private (int Start, int End) GetSelection() =>
            SourceText.Document is null || lineStarts.Count == 0
                ? (0, 0)
                : (GetOffset(SourceText.Selection.Start), GetOffset(SourceText.Selection.End));

        private int GetOffset(TextPointer pointer)
        {
            if (pointer.Paragraph is not { } paragraph)
                return pointer.CompareTo(SourceText.Document.ContentEnd) >= 0 ? viewModel.Text.Length : 0;

            var index = SourceText.Document.Blocks.ToList().IndexOf(paragraph);
            if (index < 0 || index >= lineStarts.Count)
                return 0;

            var offset = lineStarts[index];
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is not Run run)
                {
                    if (pointer.CompareTo(inline.ElementEnd) < 0)
                        return offset;
                    continue;
                }

                if (pointer.CompareTo(run.ContentStart) <= 0)
                    return offset;
                if (pointer.CompareTo(run.ContentEnd) <= 0)
                    return offset + run.ContentStart.GetOffsetToPosition(pointer);
                offset += run.Text.Length;
            }

            return offset;
        }

        private TextPointer GetPointer(int offset)
        {
            var blocks = SourceText.Document.Blocks.OfType<Paragraph>().ToList();
            var index = Math.Max(0, lineStarts.FindLastIndex(start => start <= offset));
            if (index >= blocks.Count)
                return SourceText.Document.ContentEnd;

            var remaining = offset - lineStarts[index];
            foreach (var run in blocks[index].Inlines.OfType<Run>())
            {
                if (remaining <= run.Text.Length)
                    return run.ContentStart.GetPositionAtOffset(remaining) ?? run.ContentEnd;
                remaining -= run.Text.Length;
            }

            return blocks[index].ContentEnd;
        }

        private void SetSelection(int start, int end)
        {
            try
            {
                SourceText.Selection.Select(GetPointer(start), GetPointer(end));
            }
            catch
            {
            }
        }

        private void AddBoundary()
        {
            var (start, end) = GetSelection();
            viewModel.AddBoundaries(start, end - start);
        }

        private void RemoveBoundary()
        {
            var (start, end) = GetSelection();
            viewModel.RemoveBoundaries(start, end - start);
        }

        private void SourceText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!viewModel.IsBoundaryMode)
                return;

            switch (e.Key)
            {
                case Key.Enter:
                    AddBoundary();
                    e.Handled = true;
                    break;
                case Key.Delete or Key.Back:
                    RemoveBoundary();
                    e.Handled = true;
                    break;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddBoundary();

        private void RemoveButton_Click(object sender, RoutedEventArgs e) => RemoveBoundary();

        private void ClearButton_Click(object sender, RoutedEventArgs e) => viewModel.ClearBoundaries();

        private void SplitButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
