using System.Collections.Immutable;
using System.Numerics;
using System.Windows.Media;
using PresetStudio.Preview;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using TextDecoration = YukkuriMovieMaker.Commons.TextDecoration;

namespace PresetStudio.Model
{
    internal sealed record TextSplitPart(TextItem Item, Vector2 Offset, bool IsSelected, int Order);

    internal enum TextSplitMode
    {
        Selection,
        Character,
        Line,
        Word,
    }

    internal sealed class TextSplitPlan
    {
        private const float ProbeOffset = 100f;
        private const float ClusterTolerance = 0.5f;
        private const int MaxClusterDepth = 2;
        private static readonly TextDecoration plain = new(0, 0, false, false, 1.0, null, null, false);
        private static readonly Style defaultStyle = new TextItem().Style;

        private readonly string originalText;
        private readonly ImmutableList<TextDecoration> originalDecorations;
        private readonly TextDecoration?[] bases;
        private readonly TextItem originalProbe;
        private readonly int frame;
        private readonly IReadOnlyList<Piece> pieces;
        private readonly Dictionary<(int, int), Vector2?> originalCentroids = [];

        private TextSplitPlan(TextItem target, string text, TextDecoration?[] bases, int frame, IReadOnlyList<Piece> pieces)
        {
            originalText = text;
            originalDecorations = target.Decorations ?? [];
            this.bases = bases;
            this.frame = Math.Clamp(frame, 0, Math.Max(0, target.Length - 1));
            originalProbe = CreateProbe(target);
            this.pieces = pieces;
        }

        public static TextSplitPlan? CreateSegments(TextItem target, TextSplitMode mode, IReadOnlyCollection<int> boundaries, int frame)
        {
            var text = target.Text ?? string.Empty;
            var segments = GetSegments(text, mode, boundaries);
            if (segments.Count < 2)
                return null;

            var bases = GetCharacterDecorations(target, text);
            var pieces = segments
                .Select(indices => new Piece(text, indices, bases, (TextItem)target.GetClone(), decoration => decoration, true))
                .ToList();
            return new TextSplitPlan(target, text, bases, frame, pieces);
        }

        public static List<List<int>> GetSegments(string text, TextSplitMode mode, IReadOnlyCollection<int> boundaries)
        {
            if (mode is TextSplitMode.Selection or TextSplitMode.Word)
                return GetBoundarySegments(text, boundaries);

            var result = new List<List<int>>();
            var lineStart = 0;
            for (var index = 0; index <= text.Length; index++)
            {
                if (index < text.Length && !IsLineBreak(text[index]))
                    continue;

                AddLineSegments(text, lineStart, index, mode, result);
                lineStart = index + 1;
            }

            return [.. result.Where(segment => HasContent(text, segment))];
        }

        public static int NormalizeBoundary(string text, int boundary)
        {
            var value = Math.Clamp(boundary, 0, text.Length);
            if (value > 0 && value < text.Length && text[value - 1] == '\r' && text[value] == '\n')
                value++;
            return value;
        }

        private static List<List<int>> GetBoundarySegments(string text, IReadOnlyCollection<int> boundaries)
        {
            var cuts = boundaries
                .Select(boundary => NormalizeBoundary(text, boundary))
                .Where(boundary => boundary > 0 && boundary < text.Length)
                .Append(0)
                .Append(text.Length)
                .Distinct()
                .Order()
                .ToList();

            var result = new List<List<int>>();
            for (var i = 0; i + 1 < cuts.Count; i++)
            {
                var segment = Trim(text, [.. Enumerable.Range(cuts[i], cuts[i + 1] - cuts[i])]);
                if (HasContent(text, segment))
                    result.Add(segment);
            }

            return result;
        }

        private static void AddLineSegments(string text, int start, int end, TextSplitMode mode, List<List<int>> result)
        {
            if (end <= start)
                return;

            if (mode == TextSplitMode.Line)
            {
                result.Add([.. Enumerable.Range(start, end - start)]);
                return;
            }

            var line = text[start..end];
            var elements = System.Globalization.StringInfo.ParseCombiningCharacters(line);
            for (var i = 0; i < elements.Length; i++)
            {
                var elementEnd = i + 1 < elements.Length ? elements[i + 1] : line.Length;
                result.Add([.. Enumerable.Range(start + elements[i], elementEnd - elements[i])]);
            }
        }

        public static bool CanSplit(TextItem target, int start, int length) =>
            TryGetIndices(target.Text ?? string.Empty, start, length, out _, out _);

        public static TextSplitPlan? Create(TextItem target, TextStylePreset? source, int start, int length, TextStyleParts parts, int frame)
        {
            var text = target.Text ?? string.Empty;
            if (!TryGetIndices(text, start, length, out var remainingIndices, out var selectedIndices))
                return null;

            var bases = GetCharacterDecorations(target, text);
            var selectedTemplate = (TextItem)target.GetClone();
            if (source is not null)
                TextStyleApplier.Apply(selectedTemplate, source, parts & ~TextStyleParts.Layout);

            var reset = source is null ? (Func<TextDecoration, TextDecoration>)(decoration => decoration) : CreateReset(parts);
            var remaining = new Piece(text, remainingIndices, bases, (TextItem)target.GetClone(), decoration => decoration, false);
            var selected = new Piece(text, selectedIndices, bases, selectedTemplate, reset, true);
            return new TextSplitPlan(target, text, bases, frame, [remaining, selected]);
        }

        public bool IsCurrent(TextItem target) =>
            target.Text == originalText && (target.Decorations ?? []).SequenceEqual(originalDecorations);

        public IReadOnlyList<TextSplitPart> Measure(PresetPreviewRenderer renderer)
        {
            var (axisX, axisY) = MeasureAxes(renderer);
            var result = new List<TextSplitPart>();
            var queue = new Queue<(Piece Piece, int Depth)>(pieces.Select(piece => (piece, 0)));
            while (queue.TryDequeue(out var entry))
            {
                var (piece, depth) = entry;
                var deltas = MeasureDeltas(renderer, piece);
                var clusters = Cluster(deltas);
                if (clusters.Count <= 1)
                {
                    var delta = deltas.FirstOrDefault(value => value is not null) ?? Vector2.Zero;
                    result.Add(new TextSplitPart(piece.CreateItem(), Solve(axisX, axisY, delta), piece.IsSelected, piece.Order));
                    continue;
                }

                var groups = depth < MaxClusterDepth ? clusters : [.. Enumerable.Range(0, piece.RunCount).Select(run => new List<int> { run })];
                foreach (var group in groups)
                {
                    if (piece.CreateSubPiece(group) is { } sub)
                        queue.Enqueue((sub, depth + 1));
                }
            }

            return [.. result.OrderBy(part => part.Order)];
        }

        public static IReadOnlyList<(IItem Item, bool IsSelected)> Apply(Timeline timeline, TextItem target, IReadOnlyList<TextSplitPart> parts, int interval = 0)
        {
            var ordered = parts.OrderBy(part => part.Order).ToList();
            var keeper = parts.FirstOrDefault(part => !part.IsSelected) ?? ordered[0];
            var others = ordered.Where(part => part != keeper).ToList();
            for (var i = 0; i < others.Count; i++)
            {
                var part = others[i];
                part.Item.Frame = target.Frame + Math.Max(0, interval) * ordered.IndexOf(part);
                part.Item.Length = target.Length;
                part.Item.Layer = i;
                Shift(part.Item, part.Offset);
            }

            var added = others.Select(part => (IItem)part.Item).ToArray();
            if (added.Length > 0 && !timeline.TryAddItems(added, added.Min(item => item.Frame), target.Layer + 1, false))
                return [];

            target.Text = keeper.Item.Text;
            target.Decorations = keeper.Item.Decorations;
            Shift(target, keeper.Offset);

            var group = target.Group != 0 ? target.Group : timeline.Items.Select(item => item.Group).DefaultIfEmpty(0).Max() + 1;
            target.Group = group;
            foreach (var item in added)
                item.Group = group;

            var selection = parts.Where(part => part.IsSelected).Select(part => part == keeper ? target : (IItem)part.Item).ToList();
            timeline.SelectedItems = [.. selection];
            return [.. parts.Select(part => (part == keeper ? (IItem)target : part.Item, part.IsSelected))];
        }

        private static void Shift(TextItem item, Vector2 offset)
        {
            if (offset.X != 0)
                item.X.AddToEachValues(offset.X);
            if (offset.Y != 0)
                item.Y.AddToEachValues(offset.Y);
        }

        private (Vector2 X, Vector2 Y) MeasureAxes(PresetPreviewRenderer renderer)
        {
            var fallback = (new Vector2(1, 0), new Vector2(0, 1));
            var all = CreateOriginalProbeDecorations(0, originalText.Length);
            var origin = MeasureProbe(renderer, originalProbe, all);
            originalProbe.X.CopyFrom(new Animation(ProbeOffset, -100000, 100000));
            var movedX = MeasureProbe(renderer, originalProbe, all);
            originalProbe.X.CopyFrom(new Animation(0, -100000, 100000));
            originalProbe.Y.CopyFrom(new Animation(ProbeOffset, -100000, 100000));
            var movedY = MeasureProbe(renderer, originalProbe, all);
            originalProbe.Y.CopyFrom(new Animation(0, -100000, 100000));

            if (origin is not { } o || movedX is not { } x || movedY is not { } y)
                return fallback;

            var axisX = (x - o) / ProbeOffset;
            var axisY = (y - o) / ProbeOffset;
            return Math.Abs(axisX.X * axisY.Y - axisY.X * axisX.Y) > 0.0001f ? (axisX, axisY) : fallback;
        }

        private static Vector2 Solve(Vector2 axisX, Vector2 axisY, Vector2 delta)
        {
            var determinant = axisX.X * axisY.Y - axisY.X * axisX.Y;
            return new Vector2(
                MathF.Round((delta.X * axisY.Y - axisY.X * delta.Y) / determinant, 2),
                MathF.Round((axisX.X * delta.Y - delta.X * axisX.Y) / determinant, 2));
        }

        private Vector2?[] MeasureDeltas(PresetPreviewRenderer renderer, Piece piece)
        {
            var probe = CreateProbe(piece.CreateItem());
            var deltas = new Vector2?[piece.RunCount];
            for (var run = 0; run < piece.RunCount; run++)
            {
                var current = MeasureProbe(renderer, probe, piece.CreateProbeDecorations(run));
                var range = piece.GetOriginalRange(run);
                if (!originalCentroids.TryGetValue(range, out var original))
                {
                    original = MeasureProbe(renderer, originalProbe, CreateOriginalProbeDecorations(range.Origin, range.Length));
                    originalCentroids[range] = original;
                }

                deltas[run] = original - current;
            }

            return deltas;
        }

        private static List<List<int>> Cluster(Vector2?[] deltas)
        {
            var clusters = new List<(Vector2 Delta, List<int> Runs)>();
            var pending = new List<int>();
            for (var run = 0; run < deltas.Length; run++)
            {
                if (deltas[run] is not { } delta)
                {
                    if (clusters.Count == 0)
                        pending.Add(run);
                    else
                        clusters[^1].Runs.Add(run);
                    continue;
                }

                var index = clusters.FindIndex(cluster => Vector2.Distance(cluster.Delta, delta) <= ClusterTolerance);
                if (index < 0)
                {
                    clusters.Add((delta, []));
                    index = clusters.Count - 1;
                }

                clusters[index].Runs.Add(run);
            }

            if (clusters.Count == 0)
                return [pending];

            clusters[0].Runs.InsertRange(0, pending);
            return [.. clusters.Select(cluster => cluster.Runs.Order().ToList())];
        }

        private Vector2? MeasureProbe(PresetPreviewRenderer renderer, TextItem probe, ImmutableList<TextDecoration> decorations)
        {
            probe.Decorations = decorations;
            return renderer.MeasureInkCentroid([probe], frame);
        }

        private ImmutableList<TextDecoration> CreateOriginalProbeDecorations(int origin, int length) => Build(
            originalText,
            index => (bases[index], origin <= index && index < origin + length),
            (index, count) =>
            {
                var decoration = (bases[index] ?? plain) with { Start = index, Length = count, IsLineBreak = false };
                return origin <= index && index < origin + length ? Show(decoration) : Hide(decoration);
            });

        private static TextItem CreateProbe(TextItem item)
        {
            var probe = (TextItem)item.GetClone();
            probe.Layer = 0;
            probe.Frame = 0;
            probe.X.CopyFrom(new Animation(0, -100000, 100000));
            probe.Y.CopyFrom(new Animation(0, -100000, 100000));
            probe.Opacity.CopyFrom(new Animation(100, 0, 100));
            probe.FadeIn = 0;
            probe.FadeOut = 0;
            probe.VideoEffects = [];
            probe.DisplayInterval = 0;
            probe.HideInterval = 0;
            probe.Style = defaultStyle;
            return probe;
        }

        private static TextDecoration Show(TextDecoration decoration) => decoration with { Foreground = Colors.White, DecorationColor = Colors.Transparent };

        private static TextDecoration Hide(TextDecoration decoration) => decoration with { Foreground = Colors.Transparent, DecorationColor = Colors.Transparent };

        private static Func<TextDecoration, TextDecoration> CreateReset(TextStyleParts parts) => decoration => decoration with
        {
            Font = parts.HasFlag(TextStyleParts.Font) ? null : decoration.Font,
            Scale = parts.HasFlag(TextStyleParts.FontSize) ? 1.0 : decoration.Scale,
            Foreground = parts.HasFlag(TextStyleParts.Color) ? null : decoration.Foreground,
            DecorationColor = parts.HasFlag(TextStyleParts.Decoration) ? null : decoration.DecorationColor,
            StyleType = parts.HasFlag(TextStyleParts.Decoration) ? null : decoration.StyleType,
            IsBold = !parts.HasFlag(TextStyleParts.Format) && decoration.IsBold,
            IsItalic = !parts.HasFlag(TextStyleParts.Format) && decoration.IsItalic,
            IsUnderline = !parts.HasFlag(TextStyleParts.Format) && decoration.IsUnderline,
            IsStrikethrough = !parts.HasFlag(TextStyleParts.Format) && decoration.IsStrikethrough,
        };

        private static bool TryGetIndices(string text, int start, int length, out List<int> remaining, out List<int> selected)
        {
            var begin = Math.Clamp(start, 0, text.Length);
            var end = Math.Clamp(start + length, begin, text.Length);
            if (begin > 0 && begin < text.Length && text[begin - 1] == '\r' && text[begin] == '\n')
                begin++;
            if (end > 0 && end < text.Length && text[end - 1] == '\r' && text[end] == '\n')
                end++;

            remaining = Trim(text, [.. Enumerable.Range(0, text.Length).Where(index => index < begin || index >= end || IsLineBreak(text[index]))]);
            selected = Trim(text, [.. Enumerable.Range(begin, Math.Max(0, end - begin))]);
            return HasContent(text, remaining) && HasContent(text, selected);
        }

        private static List<int> Trim(string text, List<int> indices)
        {
            while (indices.Count > 0 && IsLineBreak(text[indices[0]]))
                indices.RemoveAt(0);
            while (indices.Count > 0 && IsLineBreak(text[indices[^1]]))
                indices.RemoveAt(indices.Count - 1);
            return indices;
        }

        private static bool HasContent(string text, List<int> indices) => indices.Any(index => !char.IsWhiteSpace(text[index]));

        private static bool IsLineBreak(char value) => value is '\r' or '\n';

        private static TextDecoration?[] GetCharacterDecorations(TextItem item, string text)
        {
            var result = new TextDecoration?[text.Length];
            foreach (var decoration in item.Decorations ?? [])
            {
                if (decoration.IsLineBreak)
                    continue;

                for (var i = Math.Max(0, decoration.Start); i < Math.Min(text.Length, decoration.Start + decoration.Length); i++)
                    result[i] ??= decoration;
            }

            return result;
        }

        private static ImmutableList<TextDecoration> Build(string text, Func<int, object> key, Func<int, int, TextDecoration> create)
        {
            var result = ImmutableList.CreateBuilder<TextDecoration>();
            var index = 0;
            while (index < text.Length)
            {
                if (IsLineBreak(text[index]))
                {
                    var size = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    result.Add(plain with { Start = index, Length = size, IsLineBreak = true });
                    index += size;
                    continue;
                }

                var current = key(index);
                var next = index + 1;
                while (next < text.Length && !IsLineBreak(text[next]) && Equals(key(next), current))
                    next++;

                result.Add(create(index, next - index));
                index = next;
            }

            return result.ToImmutable();
        }

        private sealed class Piece
        {
            private readonly string originalText;
            private readonly TextDecoration?[] bases;
            private readonly TextItem template;
            private readonly Func<TextDecoration, TextDecoration> transform;
            private readonly int[] origins;
            private readonly int[] runOf;
            private readonly List<int> runStarts = [];

            public string Text { get; }

            public bool IsSelected { get; }

            public int Order => origins.Length > 0 ? origins[0] : 0;

            public int RunCount => runStarts.Count;

            private ImmutableList<TextDecoration> Decorations { get; }

            public Piece(string originalText, IReadOnlyList<int> indices, TextDecoration?[] bases, TextItem template, Func<TextDecoration, TextDecoration> transform, bool isSelected)
            {
                this.originalText = originalText;
                this.bases = bases;
                this.template = template;
                this.transform = transform;
                IsSelected = isSelected;
                origins = [.. indices];
                Text = string.Concat(origins.Select(index => originalText[index]));
                runOf = new int[origins.Length];
                for (var i = 0; i < origins.Length; i++)
                {
                    if (IsLineBreak(Text[i]))
                    {
                        runOf[i] = -1;
                        continue;
                    }

                    if (i > 0 && runOf[i - 1] >= 0 && origins[i] == origins[i - 1] + 1)
                    {
                        runOf[i] = runOf[i - 1];
                        continue;
                    }

                    runOf[i] = runStarts.Count;
                    runStarts.Add(i);
                }

                Decorations = Build(
                    Text,
                    index => (bases[origins[index]], runOf[index]),
                    (index, count) => transform((bases[origins[index]] ?? plain) with { Start = index, Length = count, IsLineBreak = false }));
            }

            public TextItem CreateItem()
            {
                var item = (TextItem)template.GetClone();
                item.Text = Text;
                item.Decorations = Decorations.Any(decoration => decoration.HasDecoration) ? Decorations : [];
                return item;
            }

            public Piece? CreateSubPiece(IReadOnlyCollection<int> runs)
            {
                var included = Enumerable.Range(0, origins.Length).Where(i => runOf[i] >= 0 && runs.Contains(runOf[i])).ToList();
                if (included.Count == 0)
                    return null;

                var first = included[0];
                var last = included[^1];
                var indices = Enumerable.Range(first, last - first + 1)
                    .Where(i => runOf[i] < 0 || runs.Contains(runOf[i]))
                    .Select(i => origins[i])
                    .ToList();
                return new Piece(originalText, indices, bases, template, transform, IsSelected);
            }

            public (int Origin, int Length) GetOriginalRange(int run)
            {
                var start = runStarts[run];
                var end = start;
                while (end < runOf.Length && runOf[end] == run)
                    end++;
                return (origins[start], end - start);
            }

            public ImmutableList<TextDecoration> CreateProbeDecorations(int run) =>
            [
                .. Decorations.Select(decoration => decoration.IsLineBreak
                    ? decoration
                    : runOf[decoration.Start] == run ? Show(decoration) : Hide(decoration)),
            ];
        }
    }
}
