using System.Collections.Concurrent;

namespace PresetStudio.Preview
{
    internal sealed class RenderWorker : IDisposable
    {
        private readonly BlockingCollection<Action<PresetPreviewRenderer?>> jobs = [];

        public PreviewVideoInfo VideoInfo { get; }

        public RenderWorker(PreviewVideoInfo videoInfo, string name)
        {
            VideoInfo = videoInfo;
            var thread = new Thread(Run) { IsBackground = true, Name = name };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public Task<T> Enqueue<T>(Func<PresetPreviewRenderer, T> job)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                jobs.Add(renderer =>
                {
                    if (renderer is null)
                    {
                        completion.SetCanceled();
                        return;
                    }

                    try
                    {
                        completion.SetResult(job(renderer));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                completion.SetCanceled();
            }

            return completion.Task;
        }

        private void Run()
        {
            PresetPreviewRenderer? renderer = null;
            try
            {
                renderer = new PresetPreviewRenderer(VideoInfo);
            }
            catch
            {
            }

            try
            {
                foreach (var job in jobs.GetConsumingEnumerable())
                    job(renderer);
            }
            finally
            {
                renderer?.Dispose();
            }
        }

        public void Dispose() => jobs.CompleteAdding();
    }
}
