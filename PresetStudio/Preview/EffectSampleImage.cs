using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PresetStudio.Settings;
using YukkuriMovieMaker.Commons;

namespace PresetStudio.Preview
{
    internal static class EffectSampleImage
    {
        private const int Width = 480;
        private const int Height = 320;

        private static string? path;

        public static string GetPath()
        {
            var customPath = PresetStudioSettings.Default.EffectPreviewImagePath;
            return customPath.Length > 0 && File.Exists(customPath) ? customPath : GetGeneratedPath();
        }

        private static string GetGeneratedPath()
        {
            if (path is not null)
                return path;

            var directory = Path.GetDirectoryName(typeof(EffectSampleImage).Assembly.Location) ?? AppDirectories.PluginDirectory;
            var filePath = Path.Combine(directory, "EffectSample.png");
            try
            {
                using var stream = File.Create(filePath);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(Draw()));
                encoder.Save(stream);
            }
            catch when (File.Exists(filePath))
            {
            }

            path = filePath;
            return path;
        }

        private static RenderTargetBitmap Draw()
        {
            var card = new Rect(60, 40, 360, 240);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var clip = new RectangleGeometry(card, 24, 24);
                context.PushClip(clip);

                context.DrawRectangle(
                    new LinearGradientBrush(Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0x7E, 0x57, 0xC2), 45),
                    null,
                    card);

                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), null, new Point(330, 110), 36, 36);

                var cloud = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
                context.DrawEllipse(cloud, null, new Point(150, 100), 34, 18);
                context.DrawEllipse(cloud, null, new Point(180, 92), 28, 22);
                context.DrawEllipse(cloud, null, new Point(205, 102), 26, 16);

                context.DrawGeometry(
                    new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)),
                    null,
                    Geometry.Parse("M 60,280 L 60,210 Q 150,150 240,210 T 420,200 L 420,280 Z"));
                context.DrawGeometry(
                    new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                    null,
                    Geometry.Parse("M 60,280 L 60,245 Q 200,195 320,240 T 420,235 L 420,280 Z"));

                context.Pop();
                context.DrawRoundedRectangle(null, new Pen(Brushes.White, 6), card, 24, 24);
            }

            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
