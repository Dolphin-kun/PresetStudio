using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PresetStudio.Model;

namespace PresetStudio.Preview
{
    internal sealed class RuleImageAnimation : IDisposable
    {
        private const double TransitionSeconds = 1.2;
        private const double HoldSeconds = 0.4;
        private const double Softness = 0.08;

        private static readonly Dictionary<(string Path, int Width, int Height), byte[]> sampleCache = [];

        private readonly BitmapSource? originalImage;
        private readonly byte[] gray;
        private readonly byte[] before;
        private readonly byte[] after;
        private readonly int width;
        private readonly int height;
        private readonly byte[] pixels;
        private readonly WriteableBitmap bitmap;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public RuleImagePreset Target { get; }

        public RuleImageAnimation(RuleImagePreset target)
        {
            Target = target;
            originalImage = target.PreviewImage ?? throw new InvalidOperationException();
            var source = new FormatConvertedBitmap(originalImage, PixelFormats.Gray8, null, 0);
            width = source.PixelWidth;
            height = source.PixelHeight;
            gray = new byte[width * height];
            source.CopyPixels(gray, width, 0);

            var (beforePath, afterPath) = TransitionSampleImage.GetPaths();
            before = LoadSample(beforePath, width, height);
            after = LoadSample(afterPath, width, height);

            pixels = new byte[width * height * 4];
            bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30), DispatcherPriority.Render, (_, _) => Draw(), Dispatcher.CurrentDispatcher);
            Draw();
            Target.PreviewImage = bitmap;
            timer.Start();
        }

        private static byte[] LoadSample(string path, int width, int height)
        {
            if (sampleCache.TryGetValue((path, width, height), out var cached))
                return cached;

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();

            var scale = Math.Max((double)width / image.PixelWidth, (double)height / image.PixelHeight) * 1.01;
            var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            var x = Math.Max(0, (scaled.PixelWidth - width) / 2);
            var y = Math.Max(0, (scaled.PixelHeight - height) / 2);
            var cropped = new CroppedBitmap(scaled, new Int32Rect(x, y, Math.Min(width, scaled.PixelWidth - x), Math.Min(height, scaled.PixelHeight - y)));
            var converted = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);

            var result = new byte[width * height * 4];
            converted.CopyPixels(new Int32Rect(0, 0, converted.PixelWidth, converted.PixelHeight), result, width * 4, 0);
            sampleCache[(path, width, height)] = result;
            return result;
        }

        private void Draw()
        {
            var cycle = TransitionSeconds + HoldSeconds;
            var time = stopwatch.Elapsed.TotalSeconds % cycle;
            var progress = Math.Min(1, time / TransitionSeconds) * (1 + Softness);

            for (var i = 0; i < gray.Length; i++)
            {
                var mix = Math.Clamp((progress - gray[i] / 255.0) / Softness, 0, 1);
                var offset = i * 4;
                for (var channel = 0; channel < 3; channel++)
                    pixels[offset + channel] = (byte)(before[offset + channel] + (after[offset + channel] - before[offset + channel]) * mix);
                pixels[offset + 3] = 255;
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        }

        public void Dispose()
        {
            timer.Stop();
            Target.PreviewImage = originalImage;
        }
    }
}
