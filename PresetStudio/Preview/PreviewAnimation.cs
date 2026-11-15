using System.Diagnostics;
using System.Windows.Media.Imaging;
using PresetStudio.Model;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Preview
{
    internal sealed class PreviewAnimation : IDisposable
    {
        private const int BoundsSampleCount = 8;

        private readonly RenderWorker worker;
        private readonly Func<IReadOnlyList<IItem>?> createItems;
        private readonly BitmapSource? originalImage;
        private readonly bool useCanvas;
        private bool isDisposed;

        public IPreviewItem Target { get; }

        private readonly bool trimsTransparent;

        public PreviewAnimation(RenderWorker worker, IPreviewItem target, Func<IReadOnlyList<IItem>?> createItems, bool useCanvas = false, bool trimsTransparent = false)
        {
            this.worker = worker;
            this.createItems = createItems;
            this.useCanvas = useCanvas;
            this.trimsTransparent = trimsTransparent;
            Target = target;
            originalImage = target.PreviewImage;
            Run();
        }

        private async void Run()
        {
            try
            {
                var prepared = await worker.Enqueue(renderer => createItems() is { } items
                    ? (Items: items, Length: Math.Max(1, renderer.GetLength(items)), Bounds: useCanvas ? renderer.CanvasBounds : trimsTransparent ? renderer.MeasureOpaqueBounds(items) : renderer.MeasureBounds(items, BoundsSampleCount))
                    : default);
                if (prepared.Items is not { } items)
                    return;

                var (_, length, bounds) = prepared;
                var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, worker.VideoInfo.FPS));
                var stopwatch = new Stopwatch();

                for (var frame = 0; !isDisposed; frame = (frame + 1) % length)
                {
                    stopwatch.Restart();
                    var currentFrame = frame;
                    var image = await worker.Enqueue(renderer => renderer.Render(items, currentFrame, bounds, useCanvas));
                    if (isDisposed)
                        return;

                    Target.PreviewImage = image;

                    var wait = interval - stopwatch.Elapsed;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait);
                }
            }
            catch
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Target.PreviewImage = originalImage;
        }
    }
}
