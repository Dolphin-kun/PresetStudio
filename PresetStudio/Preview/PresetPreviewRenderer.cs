using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows.Media.Imaging;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Controls;
using PresetStudio.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Plugin.Transition;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using AlphaMode = Vortice.DCommon.AlphaMode;
using FileGroupType = YukkuriMovieMaker.Settings.FileGroupType;

namespace PresetStudio.Preview
{
    internal sealed class PresetPreviewRenderer : IDisposable
    {
        private const int RenderScale = 2;
        private const int ThumbnailWidth = 160 * RenderScale;
        private const int ThumbnailHeight = 90 * RenderScale;
        private const float ContentMargin = 10 * RenderScale;
        private const int EffectPreviewSeconds = 3;

        private readonly Timeline timeline;
        private readonly TimelineSourceAndDevices source;
        private IReadOnlyList<IItem>? currentItems;

        public PresetPreviewRenderer(PreviewVideoInfo videoInfo)
        {
            timeline = new Timeline(
                "PresetStudioPreview",
                videoInfo.Width,
                videoInfo.Height,
                videoInfo.FPS,
                videoInfo.Hz,
                new TimelineVerticalLine());
            timeline.VideoInfo.BackgroundColor = System.Windows.Media.Colors.Transparent;

            var scenes = new Scenes(isUndoEnabled: false);
            scenes.AddScene(timeline);
            source = new TimelineSourceAndDevices(scenes.AllScenes.First());
        }

        private static readonly FileGroupType[] imageFileGroups =
        [
            FileGroupType.ImageItem,
            FileGroupType.Texture,
            FileGroupType.Thumbnail,
            FileGroupType.TachieParts,
        ];

        public static IReadOnlyList<IItem> CreateEffectPreviewItems(IEnumerable<IVideoEffect> effects, int fps)
        {
            var imagePath = EffectSampleImage.GetPath();
            return
            [
                new ImageItem(imagePath)
                {
                    Frame = 0,
                    Layer = 0,
                    Length = fps * EffectPreviewSeconds,
                    VideoEffects = [.. effects.Select(effect => FillEmptyImagePaths(effect, imagePath))],
                },
            ];
        }

        private static IVideoEffect FillEmptyImagePaths(IVideoEffect effect, string imagePath)
        {
            foreach (var property in effect.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(string)
                    && property.CanWrite
                    && property.GetCustomAttribute<FileSelectorAttribute>() is { } selector
                    && imageFileGroups.Contains(selector.FileGroup)
                    && string.IsNullOrEmpty(property.GetValue(effect) as string))
                {
                    property.SetValue(effect, imagePath);
                }
            }

            return effect;
        }

        public static IReadOnlyList<IItem> CreateTransitionPreviewItems(ITransitionPlugin plugin, PreviewVideoInfo videoInfo)
        {
            var (beforePath, afterPath) = TransitionSampleImage.GetPaths();
            var lead = Math.Max(1, videoInfo.FPS / 2);
            var length = Math.Max(1, videoInfo.FPS);
            var tail = Math.Max(1, lead - length * 3 / 10);
            var transition = new TransitionItem
            {
                Frame = lead,
                Layer = 1,
                Length = length,
                TransitionType = plugin.GetType(),
                TransitionParameter = plugin.CreateTransitionParameter(),
            };

            return
            [
                CreateCoverImageItem(beforePath, videoInfo, 0, lead),
                CreateCoverImageItem(afterPath, videoInfo, lead, length + tail),
                transition,
            ];
        }

        public static IReadOnlyList<IItem> CreateTachiePreviewItems(Character character, TachieItem? source, FacePreset? face, int fps)
        {
            var length = Math.Max(1, fps * 3);
            var tachie = source is null ? new TachieItem(character) : (TachieItem)source.GetClone();
            tachie.Character = character;
            tachie.Frame = 0;
            tachie.Layer = 0;
            tachie.Length = length;

            if (face?.FaceParameter is not { } parameter)
                return [tachie];

            var faceItem = new TachieFaceItem(character)
            {
                Frame = 0,
                Layer = 1,
                Length = length,
                TachieFaceParameter = YukkuriMovieMaker.Json.Json.GetClone(parameter)!,
                TachieFaceEffects = [.. face.FaceEffects.Select(effect => YukkuriMovieMaker.Json.Json.GetClone(effect)).OfType<IVideoEffect>()],
            };
            return [tachie, faceItem];
        }

        private static ImageItem CreateCoverImageItem(string path, PreviewVideoInfo videoInfo, int frame, int length)
        {
            var item = new ImageItem(path) { Frame = frame, Layer = 0, Length = length };
            try
            {
                var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var image = decoder.Frames[0];
                var zoom = Math.Max((double)videoInfo.Width / image.PixelWidth, (double)videoInfo.Height / image.PixelHeight) * 100;
                item.Zoom.CopyFrom(new Animation(zoom, 0, 100000));
            }
            catch
            {
            }

            return item;
        }

        public static IReadOnlyList<IItem> CreatePreviewItems(IEnumerable<IItem> items, string text)
        {
            var clones = items.Select(item => item.GetClone()).ToList();
            foreach (var textItem in clones.OfType<TextItem>())
                textItem.Text = text;

            var minFrame = clones.Select(item => item.Frame).DefaultIfEmpty(0).Min();
            var minLayer = clones.Select(item => item.Layer).DefaultIfEmpty(0).Min();
            foreach (var item in clones)
            {
                item.Frame -= minFrame;
                item.Layer -= minLayer;
            }

            return clones;
        }

        public int GetLength(IReadOnlyList<IItem> previewItems)
        {
            SetItems(previewItems);
            return timeline.Length;
        }

        public RawRectF CanvasBounds => new(
            -timeline.VideoInfo.Width / 2f,
            -timeline.VideoInfo.Height / 2f,
            timeline.VideoInfo.Width / 2f,
            timeline.VideoInfo.Height / 2f);

        public BitmapSource Render(IReadOnlyList<IItem> previewItems, int? frame, RawRectF? bounds = null, bool fillBounds = false)
        {
            SetItems(previewItems);
            Update(frame ?? timeline.Length / 2);
            return RenderThumbnail(bounds ?? GetContentBounds(), fillBounds);
        }

        public BitmapSource RenderCanvas(IReadOnlyList<IItem> previewItems, int? frame) => Render(previewItems, frame, CanvasBounds, fillBounds: true);

        public RawRectF? MeasureBounds(IReadOnlyList<IItem> previewItems, int sampleCount)
        {
            SetItems(previewItems);

            RawRectF? result = null;
            var length = Math.Max(1, timeline.Length);
            for (var i = 0; i < sampleCount; i++)
            {
                Update(length * i / sampleCount);
                if (GetContentBounds() is not { } bounds)
                    continue;

                result = result is { } union
                    ? new RawRectF(
                        Math.Min(union.Left, bounds.Left),
                        Math.Min(union.Top, bounds.Top),
                        Math.Max(union.Right, bounds.Right),
                        Math.Max(union.Bottom, bounds.Bottom))
                    : bounds;
            }

            return result;
        }

        public RawRectF? MeasureOpaqueBounds(IReadOnlyList<IItem> previewItems, int? frame = null)
        {
            SetItems(previewItems);
            Update(frame ?? timeline.Length / 2);
            if (GetContentBounds() is not { } bounds)
                return null;

            const float maxSize = 512;
            var scale = Math.Min(1f, maxSize / Math.Max(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            var width = Math.Max(1, (int)Math.Ceiling((bounds.Right - bounds.Left) * scale));
            var height = Math.Max(1, (int)Math.Ceiling((bounds.Bottom - bounds.Top) * scale));
            var context = source.Devices.DeviceContext;
            var size = new SizeI(width, height);
            var format = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);
            using var target = context.CreateBitmap(size, new BitmapProperties1(format, 96, 96, BitmapOptions.Target));
            using var readable = context.CreateBitmap(size, new BitmapProperties1(format, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));

            try
            {
                context.Target = target;
                context.BeginDraw();
                context.Clear(new Color4(0, 0, 0, 0));
                context.Transform = Matrix3x2.CreateTranslation(-bounds.Left, -bounds.Top) * Matrix3x2.CreateScale(scale);
                context.DrawImage(source.Output);
                context.Transform = Matrix3x2.Identity;
                context.EndDraw();
            }
            finally
            {
                context.Target = null;
            }

            readable.CopyFromBitmap(target);
            var mapped = readable.Map(MapOptions.Read);
            try
            {
                var row = new byte[width * 4];
                int minX = width, minY = height, maxX = -1, maxY = -1;
                for (var y = 0; y < height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(mapped.Bits + y * mapped.Pitch, row, 0, row.Length);
                    for (var x = 0; x < width; x++)
                    {
                        if (row[x * 4 + 3] < 8)
                            continue;

                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }

                if (maxX < 0)
                    return null;

                return new RawRectF(
                    bounds.Left + minX / scale,
                    bounds.Top + minY / scale,
                    bounds.Left + (maxX + 1) / scale,
                    bounds.Top + (maxY + 1) / scale);
            }
            finally
            {
                readable.Unmap();
            }
        }

        public Vector2? MeasureInkCentroid(IReadOnlyList<IItem> previewItems, int frame)
        {
            SetItems(previewItems);
            Update(frame);

            var context = source.Devices.DeviceContext;
            var image = source.Output;
            var bounds = context.GetImageLocalBounds(image);
            var left = (int)Math.Floor(bounds.Left);
            var top = (int)Math.Floor(bounds.Top);
            var width = (int)Math.Ceiling(bounds.Right) - left;
            var height = (int)Math.Ceiling(bounds.Bottom) - top;
            if (width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024)
                return null;

            var size = new SizeI(width, height);
            var format = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);
            using var target = context.CreateBitmap(size, new BitmapProperties1(format, 96, 96, BitmapOptions.Target));
            using var readable = context.CreateBitmap(size, new BitmapProperties1(format, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));

            try
            {
                context.Target = target;
                context.BeginDraw();
                context.Clear(new Color4(0, 0, 0, 0));
                context.Transform = Matrix3x2.CreateTranslation(-left, -top);
                context.DrawImage(image);
                context.Transform = Matrix3x2.Identity;
                context.EndDraw();
            }
            finally
            {
                context.Target = null;
            }

            readable.CopyFromBitmap(target);
            var mapped = readable.Map(MapOptions.Read);
            try
            {
                var row = new byte[width * 4];
                double sum = 0, sumX = 0, sumY = 0;
                for (var y = 0; y < height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(mapped.Bits + y * mapped.Pitch, row, 0, row.Length);
                    for (var x = 0; x < width; x++)
                    {
                        var alpha = row[x * 4 + 3];
                        if (alpha == 0)
                            continue;

                        sum += alpha;
                        sumX += alpha * (x + 0.5);
                        sumY += alpha * (y + 0.5);
                    }
                }

                return sum > 0 ? new Vector2((float)(sumX / sum + left), (float)(sumY / sum + top)) : null;
            }
            finally
            {
                readable.Unmap();
            }
        }

        private void Update(int frame) => source.Update(timeline.VideoInfo.GetTimeFrom(frame), TimelineSourceUsage.Paused);

        private RawRectF? GetContentBounds()
        {
            var halfWidth = timeline.VideoInfo.Width / 2f;
            var halfHeight = timeline.VideoInfo.Height / 2f;
            var bounds = source.Devices.DeviceContext.GetImageLocalBounds(source.Output);
            var left = Math.Max(bounds.Left, -halfWidth);
            var top = Math.Max(bounds.Top, -halfHeight);
            var right = Math.Min(bounds.Right, halfWidth);
            var bottom = Math.Min(bounds.Bottom, halfHeight);

            return right > left && bottom > top ? new RawRectF(left, top, right, bottom) : null;
        }

        private void SetItems(IReadOnlyList<IItem> items)
        {
            if (ReferenceEquals(currentItems, items))
                return;

            currentItems = items;
            timeline.Items = [.. items];
            timeline.RefreshTimelineLengthAndMaxLayer();
        }

        private BitmapImage RenderThumbnail(RawRectF? bounds, bool fillBounds)
        {
            var context = source.Devices.DeviceContext;
            var image = source.Output;

            using var target = context.CreateBitmap(
                new SizeI(ThumbnailWidth, ThumbnailHeight),
                new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                    96,
                    96,
                    BitmapOptions.Target));

            try
            {
                context.Target = target;
                context.BeginDraw();
                context.Clear(new Color4(0, 0, 0, 0));

                if (bounds is { } rect)
                {
                    var scale = Math.Min(RenderScale, Math.Min(
                        (ThumbnailWidth - ContentMargin * 2) / (rect.Right - rect.Left),
                        (ThumbnailHeight - ContentMargin * 2) / (rect.Bottom - rect.Top)));
                    var center = new Vector2((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
                    context.Transform =
                        Matrix3x2.CreateTranslation(-center)
                        * Matrix3x2.CreateScale(scale)
                        * Matrix3x2.CreateTranslation(ThumbnailWidth / 2f, ThumbnailHeight / 2f);
                    if (fillBounds)
                    {
                        using var brush = context.CreateSolidColorBrush(new Color4(0.5f, 0.5f, 0.5f, 0.25f));
                        context.FillRectangle(rect, brush);
                    }

                    context.DrawImage(image, interpolationMode: InterpolationMode.HighQualityCubic);
                    context.Transform = Matrix3x2.Identity;
                }

                context.EndDraw();
            }
            finally
            {
                context.Target = null;
            }

            return CreateBitmapSource(target);
        }

        private BitmapImage CreateBitmapSource(ID2D1Bitmap1 bitmap)
        {
            using var stream = new MemoryStream();
            using var factory = new Vortice.WIC.IWICImagingFactory2();
            using var encoder = factory.CreateEncoder(Vortice.WIC.ContainerFormat.Bmp);
            using var imageEncoder = factory.CreateImageEncoder(source.Devices.D2D.Device);

            encoder.Initialize(stream);
            using var frame = encoder.CreateNewFrame(out var options);
            try
            {
                frame.Initialize(null);
                var parameters = new Vortice.WIC.WICImageParameters
                {
                    PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                    DpiX = 96,
                    DpiY = 96,
                    PixelWidth = bitmap.PixelSize.Width,
                    PixelHeight = bitmap.PixelSize.Height,
                };
                imageEncoder.WriteFrame(bitmap, frame, ref parameters);
                frame.Commit();
                encoder.Commit();
            }
            finally
            {
                options?.Dispose();
            }

            stream.Position = 0;
            var result = new BitmapImage();
            result.BeginInit();
            result.StreamSource = stream;
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.EndInit();
            result.Freeze();
            return result;
        }

        public void Dispose() => ((IDisposable)source).Dispose();
    }
}
