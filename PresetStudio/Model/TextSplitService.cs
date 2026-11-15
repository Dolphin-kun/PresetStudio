using PresetStudio.Preview;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal enum TextSplitResult
    {
        Done,
        NotSplittable,
        Busy,
        Changed,
        Failed,
    }

    internal static class TextSplitService
    {
        private static bool isRunning;

        public static bool IsRunning => isRunning;

        public static Task<TextSplitResult> SplitAsync(Timeline timeline, TextItem target, TextStylePreset? source, int start, int length, TextStyleParts parts, Action beginEdit, Action endEdit) =>
            RunAsync(timeline, target, TextSplitPlan.Create(target, source, start, length, parts, timeline.CurrentFrame - target.Frame), 0, source, parts, beginEdit, endEdit);

        public static Task<TextSplitResult> SplitSegmentsAsync(Timeline timeline, TextItem target, TextSplitMode mode, IReadOnlyCollection<int> boundaries, int interval, Action beginEdit, Action endEdit) =>
            RunAsync(timeline, target, TextSplitPlan.CreateSegments(target, mode, boundaries, timeline.CurrentFrame - target.Frame), interval, null, TextStyleParts.None, beginEdit, endEdit);

        private static async Task<TextSplitResult> RunAsync(Timeline timeline, TextItem target, TextSplitPlan? plan, int interval, TextStylePreset? source, TextStyleParts parts, Action beginEdit, Action endEdit)
        {
            if (isRunning)
                return TextSplitResult.Busy;

            if (plan is null)
                return TextSplitResult.NotSplittable;

            isRunning = true;
            IReadOnlyList<TextSplitPart> splitParts;
            try
            {
                var videoInfo = new PreviewVideoInfo(
                    Math.Max(3840, timeline.VideoInfo.Width),
                    Math.Max(2160, timeline.VideoInfo.Height),
                    timeline.VideoInfo.FPS,
                    timeline.VideoInfo.Hz);
                using var worker = new RenderWorker(videoInfo, "PresetStudio text split");
                splitParts = await worker.Enqueue(plan.Measure);
            }
            catch
            {
                return TextSplitResult.Failed;
            }
            finally
            {
                isRunning = false;
            }

            if (splitParts.Count == 0)
                return TextSplitResult.Failed;

            if (!plan.IsCurrent(target) || !timeline.Items.Contains(target))
                return TextSplitResult.Changed;

            beginEdit();
            try
            {
                foreach (var (item, isSelected) in TextSplitPlan.Apply(timeline, target, splitParts, interval))
                {
                    if (isSelected && source is not null)
                        PresetLinkStore.Link(item, source, parts);
                    else if (!ReferenceEquals(item, target))
                        PresetLinkStore.CopyLink(target, item);
                }
            }
            finally
            {
                endEdit();
            }

            return TextSplitResult.Done;
        }

        public static string? GetMessage(TextSplitResult result) => result switch
        {
            TextSplitResult.Changed => Texts.SplitChanged,
            TextSplitResult.Failed => Texts.SplitFailed,
            _ => null,
        };
    }
}
