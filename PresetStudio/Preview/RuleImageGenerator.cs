using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PresetStudio.Model;

namespace PresetStudio.Preview
{
    internal static class RuleImageGenerator
    {
        public const int OutputSize = 2000;
        public const string CreatorName = "PresetStudio";

        public static BitmapSource Create(RuleImageParameters parameters, int size)
        {
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, Generate(parameters, size), size);
            bitmap.Freeze();
            return bitmap;
        }

        public static void Save(string path, RuleImageParameters parameters)
        {
            var metadata = new BitmapMetadata("png");
            metadata.SetQuery("/[0]tEXt/{str=Author}", CreatorName);
            metadata.SetQuery("/[1]tEXt/{str=Software}", CreatorName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Create(parameters, OutputSize), null, metadata, null));

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static byte[] Generate(RuleImageParameters parameters, int size)
        {
            var pixels = new byte[size * size];
            var radian = parameters.Angle * Math.PI / 180;
            var cos = Math.Cos(radian);
            var sin = Math.Sin(radian);
            var reach = Math.Abs(cos) + Math.Abs(sin);
            var count = Math.Max(1, parameters.Count);
            var noise = Math.Clamp(parameters.Noise / 100, 0, 1);
            var seed = parameters.Seed;

            Parallel.For(0, size, y =>
            {
                var v = (y + 0.5) / size * 2 - 1;
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5) / size * 2 - 1;
                    var linear = ((u * cos + v * sin) / reach + 1) / 2;
                    var radial = Math.Sqrt(u * u + v * v) / Math.Sqrt(2);
                    var clock = Frac((Math.Atan2(u, -v) - radian) / (2 * Math.PI));

                    var value = parameters.Shape switch
                    {
                        RuleImageShape.Radial => radial,
                        RuleImageShape.Rectangle => Math.Max(Math.Abs(u), Math.Abs(v)),
                        RuleImageShape.Diamond => (Math.Abs(u) + Math.Abs(v)) / 2,
                        RuleImageShape.Clock => clock,
                        RuleImageShape.Blinds => Frac(linear * count),
                        RuleImageShape.Checker => Checker(u, v, cos, sin, count),
                        RuleImageShape.Rings => Frac(radial * count),
                        RuleImageShape.Spiral => (clock + Math.Floor(radial * count)) / count,
                        RuleImageShape.Random => Hash((int)Math.Floor((u + 1) / 2 * count), (int)Math.Floor((v + 1) / 2 * count), seed),
                        _ => linear,
                    };

                    if (noise > 0)
                        value = value * (1 - noise) + Hash(x, y, seed) * noise;

                    if (parameters.IsInverted)
                        value = 1 - value;

                    pixels[y * size + x] = (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
                }
            });

            return pixels;
        }

        private static double Checker(double u, double v, double cos, double sin, int count)
        {
            var s = ((u * cos + v * sin) + 1.5) / 3 * count * 1.5;
            var t = ((-u * sin + v * cos) + 1.5) / 3 * count * 1.5;
            var parity = ((int)Math.Floor(s) + (int)Math.Floor(t)) & 1;
            return (Frac(s) + parity) / 2;
        }

        private static double Frac(double value) => value - Math.Floor(value);

        private static double Hash(int x, int y, int seed)
        {
            var hash = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFF) / 16777216.0;
        }
    }
}
