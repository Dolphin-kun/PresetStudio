using System.Windows;
using PresetStudio.Model;
using PresetStudio.Settings;
using PresetStudio.View;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.ViewModel
{
    internal sealed record TextSplitRequest(TextSplitMode Mode, IReadOnlyList<int> Boundaries, int Interval);

    internal sealed class TextSplitViewModel : Bindable
    {
        private readonly SortedSet<int> boundaries = [];

        public string Text { get; }

        public IReadOnlyList<SplitModeOption> Modes { get; } =
        [
            new(TextSplitMode.Selection, Texts.SplitModeBoundaries),
            new(TextSplitMode.Character, Texts.SplitModeCharacter),
            new(TextSplitMode.Line, Texts.SplitModeLine),
        ];

        public TextSplitMode Mode
        {
            get => PresetStudioSettings.Default.SplitMode is TextSplitMode.Word ? TextSplitMode.Selection : PresetStudioSettings.Default.SplitMode;
            set
            {
                if (Mode == value)
                    return;

                PresetStudioSettings.Default.SplitMode = value;
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(IsBoundaryMode));
                OnSegmentsChanged();
            }
        }

        public int Interval
        {
            get => PresetStudioSettings.Default.SplitInterval;
            set
            {
                if (PresetStudioSettings.Default.SplitInterval == value)
                    return;

                PresetStudioSettings.Default.SplitInterval = value;
                OnPropertyChanged(nameof(Interval));
            }
        }

        public bool IsBoundaryMode => Mode == TextSplitMode.Selection;

        public IReadOnlyList<List<int>> Segments { get; private set; } = [];

        public string SummaryText => Segments.Count >= 2 ? string.Format(Texts.SplitSummary, Segments.Count) : Texts.SplitAddBoundaryHint;

        public bool CanSplit => Segments.Count >= 2;

        public event Action? SegmentsChanged;

        private TextSplitViewModel(TextItem target, (int Start, int Length)? range)
        {
            Text = target.Text ?? string.Empty;
            if (range is { Length: > 0 } selection)
                AddBoundaries(selection.Start, selection.Length);
            else
                OnSegmentsChanged();
        }

        public void AddBoundaries(int start, int length)
        {
            boundaries.Add(TextSplitPlan.NormalizeBoundary(Text, start));
            if (length > 0)
                boundaries.Add(TextSplitPlan.NormalizeBoundary(Text, start + length));
            OnSegmentsChanged();
        }

        public void RemoveBoundaries(int start, int length)
        {
            var from = TextSplitPlan.NormalizeBoundary(Text, start);
            var to = TextSplitPlan.NormalizeBoundary(Text, start + length);
            boundaries.RemoveWhere(boundary => boundary >= from && boundary <= to);
            OnSegmentsChanged();
        }

        public void ClearBoundaries()
        {
            boundaries.Clear();
            OnSegmentsChanged();
        }

        private void OnSegmentsChanged()
        {
            Segments = TextSplitPlan.GetSegments(Text, Mode, boundaries);
            OnPropertyChanged(nameof(Segments));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(CanSplit));
            SegmentsChanged?.Invoke();
        }

        public static TextSplitRequest? Show(TextItem target, (int Start, int Length)? range, Window? owner)
        {
            var viewModel = new TextSplitViewModel(target, range);
            var window = new TextSplitWindow(viewModel)
            {
                Owner = owner ?? Application.Current?.MainWindow,
            };
            if (window.ShowDialog() != true || !viewModel.CanSplit)
                return null;

            PresetStudioSettings.Default.Save();
            return new TextSplitRequest(viewModel.Mode, [.. viewModel.boundaries], viewModel.Interval);
        }
    }
}
