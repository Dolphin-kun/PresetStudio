using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;

namespace PresetStudio.Preview
{
    internal static class TransitionSampleImage
    {
        private const int Width = 1920;
        private const int Height = 1080;

        private static (string Before, string After)? paths;

        public static (string Before, string After) GetPaths()
        {
            var settings = PresetStudio.Settings.PresetStudioSettings.Default;
            var (before, after) = GetDefaultPaths();
            return (
                settings.TransitionBeforeImagePath.Length > 0 && File.Exists(settings.TransitionBeforeImagePath) ? settings.TransitionBeforeImagePath : before,
                settings.TransitionAfterImagePath.Length > 0 && File.Exists(settings.TransitionAfterImagePath) ? settings.TransitionAfterImagePath : after);
        }

        private static (string Before, string After) GetDefaultPaths()
        {
            if (paths is { } cached)
                return cached;

            var directory = Path.GetDirectoryName(typeof(TransitionSampleImage).Assembly.Location) ?? AppDirectories.PluginDirectory;
            var before = Path.Combine(directory, "TransitionSampleBefore.png");
            var after = Path.Combine(directory, "TransitionSampleAfter.png");
            Write(before, isNight: false);
            Write(after, isNight: true);

            paths = (before, after);
            return (before, after);
        }

        private static void Write(string path, bool isNight)
        {
            if (File.Exists(path))
                return;

            try
            {
                using var stream = File.Create(path);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(Draw(isNight)));
                encoder.Save(stream);
            }
            catch
            {
            }
        }

        private static RenderTargetBitmap Draw(bool isNight)
        {
            var rect = new Rect(0, 0, Width, Height);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    isNight
                        ? new LinearGradientBrush(Color.FromRgb(0x1A, 0x23, 0x7E), Color.FromRgb(0x4A, 0x14, 0x8C), 90)
                        : new LinearGradientBrush(Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0xB3, 0xE5, 0xFC), 90),
                    null,
                    rect);

                if (isNight)
                {
                    context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0x9D)), null, new Point(1420, 300), 120, 120);
                    var star = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
                    foreach (var (x, y) in new[] { (300, 180), (520, 320), (760, 140), (980, 260), (1180, 120), (1700, 200), (200, 420), (860, 420) })
                        context.DrawEllipse(star, null, new Point(x, y), 6, 6);
                }
                else
                {
                    context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), null, new Point(1440, 280), 140, 140);
                    var cloud = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF));
                    context.DrawEllipse(cloud, null, new Point(480, 260), 150, 80);
                    context.DrawEllipse(cloud, null, new Point(620, 220), 130, 100);
                    context.DrawEllipse(cloud, null, new Point(740, 270), 120, 70);
                }

                context.DrawGeometry(
                    new SolidColorBrush(isNight ? Color.FromRgb(0x1B, 0x5E, 0x20) : Color.FromRgb(0x43, 0xA0, 0x47)),
                    null,
                    Geometry.Parse("M 0,1080 L 0,760 Q 480,560 960,760 T 1920,720 L 1920,1080 Z"));
                context.DrawGeometry(
                    new SolidColorBrush(isNight ? Color.FromRgb(0x0D, 0x3B, 0x12) : Color.FromRgb(0x2E, 0x7D, 0x32)),
                    null,
                    Geometry.Parse("M 0,1080 L 0,900 Q 640,720 1280,880 T 1920,860 L 1920,1080 Z"));
            }

            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
