using System.Collections.Immutable;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PsdParser;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource.Psd;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Plugin.Tachie.Psd;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace PresetStudio.Model
{
    internal static class FacePartProvider
    {
        private const string AnimationFaceTypeName = "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.FaceParameter";
        private static string NoneName => Texts.None;
        private const int ThumbnailSize = 120;

        private static readonly (string Folder, string Property)[] animationParts =
        [
            ("眉", "Eyebrow"),
            ("目", "Eye"),
            ("口", "Mouth"),
            ("髪", "Hair"),
            ("顔色", "Complexion"),
            ("体", "Body"),
            ("後", "Back1"),
            ("他", "Etc1"),
        ];

        private static readonly Dictionary<string, (DateTime WriteTime, PsdTree Tree)> psdCache = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<List<FacePartOption>> CreateOptionsAsync(Character character, ITachieFaceParameter face)
        {
            try
            {
                if (face is IPsdTachieParameter && character.TachieCharacterParameter is PsdTachieCharacterParameter { FilePath: { } psdPath } && File.Exists(psdPath))
                    return await CreatePsdOptionsAsync(character, face, psdPath);

                if (face.GetType().FullName == AnimationFaceTypeName)
                    return await Task.Run(() => CreateAnimationOptions(character, face));
            }
            catch
            {
            }

            return [];
        }

        private static List<FacePartOption> CreateAnimationOptions(Character character, ITachieFaceParameter face)
        {
            var directory = (character.TachieCharacterParameter as YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.CharacterParameter)?.Directory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return [];

            var result = new List<FacePartOption>();
            foreach (var (folder, propertyName) in animationParts)
            {
                var property = face.GetType().GetProperty(propertyName);
                var partDirectory = Path.Combine(directory, folder);
                if (property is null || !Directory.Exists(partDirectory))
                    continue;

                var files = Directory.GetFiles(partDirectory)
                    .Where(IsPartFile)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                foreach (var value in files.Prepend(null))
                {
                    result.Add(new FacePartOption(
                        folder,
                        value is null ? NoneName : Path.GetFileNameWithoutExtension(value),
                        face,
                        source =>
                        {
                            var clone = YmmJson.GetClone(source);
                            property.SetValue(clone, value);
                            return clone;
                        },
                        source => string.Equals(property.GetValue(source) as string, value, StringComparison.OrdinalIgnoreCase),
                        loadImage: value is null ? null : () => ShellThumbnail.LoadCroppedThumbnailAsync(value, 1000, ThumbnailSize)));
                }
            }

            return result;
        }

        private static bool IsPartFile(string path)
        {
            if (Path.GetFileName(path).Count(c => c == '.') > 1)
                return false;

            var fileType = SettingsBase<FileSettings>.Default.FileExtensions.GetFileType(path);
            return fileType.HasFlag(FileType.画像) || fileType.HasFlag(FileType.動画);
        }

        private static async Task<List<FacePartOption>> CreatePsdOptionsAsync(Character character, ITachieFaceParameter face, string psdPath)
        {
            var tree = await Task.Run(() => LoadPsdTree(psdPath));
            var result = new List<FacePartOption>();

            foreach (var preset in PsdFileSettings.LoadFromPsdFilePath(psdPath).Presets)
            {
                var ids = tree.Resolve(preset.Layers, preset.LayerPaths);
                result.Add(new FacePartOption(
                    Texts.Preset,
                    preset.Name,
                    face,
                    source => SetPsdLayers(source, psdPath, tree, ids),
                    source => GetPsdLayers(source, psdPath, tree).SetEquals(ids)));
            }

            foreach (var folder in tree.Folders.Where(folder => folder.Children.Count >= 2))
            {
                var siblings = folder.Children.Select(child => child.Id).ToList();
                result.Add(new FacePartOption(
                    folder.DisplayPath,
                    NoneName,
                    face,
                    source =>
                    {
                        var ids = GetPsdLayers(source, psdPath, tree);
                        ids.ExceptWith(siblings);
                        return SetPsdLayers(source, psdPath, tree, ids);
                    },
                    source => !GetPsdLayers(source, psdPath, tree).Overlaps(siblings)));

                foreach (var child in folder.Children)
                {
                    result.Add(new FacePartOption(
                        folder.DisplayPath,
                        child.Name,
                        face,
                        source =>
                        {
                            var ids = GetPsdLayers(source, psdPath, tree);
                            ids.ExceptWith(siblings);
                            ids.Add(child.Id);
                            return SetPsdLayers(source, psdPath, tree, ids);
                        },
                        source => GetPsdLayers(source, psdPath, tree).Contains(child.Id),
                        tree.Thumbnails.GetValueOrDefault(child.Id)));
                }
            }

            return result;
        }

        private static HashSet<string> GetPsdLayers(ITachieFaceParameter face, string psdPath, PsdTree tree)
        {
            var isEnabled = face.GetType().GetProperty("IsEnabled")?.GetValue(face) is true;
            if (face is IPsdTachieParameter psd && isEnabled && string.Equals(psd.FilePath, psdPath, StringComparison.OrdinalIgnoreCase))
                return [.. tree.Resolve(psd.EnableLayers, psd.EnableLayerPaths)];

            return [.. tree.DefaultEnabled];
        }

        private static ITachieFaceParameter? SetPsdLayers(ITachieFaceParameter face, string psdPath, PsdTree tree, IEnumerable<string> ids)
        {
            if (YmmJson.GetClone(face) is not { } clone || clone is not IPsdTachieParameter psd)
                return null;

            var list = ids.ToImmutableList();
            psd.FilePath = psdPath;
            psd.EnableLayers = list;
            psd.EnableLayerPaths = [.. list.Select(id => tree.PathOf.GetValueOrDefault(id, string.Empty))];
            clone.GetType().GetProperty("IsEnabled")?.SetValue(clone, true);
            return clone;
        }

        private static PsdTree LoadPsdTree(string path)
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            lock (psdCache)
            {
                if (psdCache.TryGetValue(path, out var cached) && cached.WriteTime == writeTime)
                    return cached.Tree;
            }

            using var psd = new PsdFile(path);
            var root = PsdFolder.Parse(psd);
            var tree = new PsdTree(root);
            lock (psdCache)
                psdCache[path] = (writeTime, tree);
            return tree;
        }

        private sealed class PsdTree
        {
            public List<PsdNode> Folders { get; } = [];

            public Dictionary<string, string> PathOf { get; } = [];

            public Dictionary<string, BitmapSource> Thumbnails { get; } = [];

            public List<string> DefaultEnabled { get; }

            private readonly Dictionary<string, string> idOfPath = [];

            public PsdTree(PsdFolder root)
            {
                DefaultEnabled = [.. root.GetEnableItems()];
                Visit(root, string.Empty);
            }

            private void Visit(PsdFolder folder, string displayPath)
            {
                var node = new PsdNode(folder.Id, folder.Name, displayPath);
                foreach (var item in folder.Items)
                {
                    PathOf[item.Id] = item.Path;
                    idOfPath[item.Path] = item.Id;
                    node.Children.Add(new PsdNode(item.Id, item.Name, displayPath));
                    if (item is PsdFolder child)
                    {
                        Visit(child, displayPath.Length == 0 ? child.Name : $"{displayPath} / {child.Name}");
                        if (child.Items.Select(x => Thumbnails.GetValueOrDefault(x.Id)).FirstOrDefault(x => x is not null) is { } first)
                            Thumbnails[item.Id] = first;
                    }
                    else if (CreateThumbnail(item) is { } thumbnail)
                    {
                        Thumbnails[item.Id] = thumbnail;
                    }
                }

                if (displayPath.Length > 0)
                    Folders.Add(node);
            }

            private static BitmapSource? CreateThumbnail(PsdItem item)
            {
                try
                {
                    if (item.LayerImageBuffer is not { Width: > 0, Height: > 0 } image)
                        return null;

                    var pixels = item.LayerMaskBuffer is { } mask ? new MaskedLayerImageBuffer(image, mask).ToBytes() : image.ToBytes();
                    var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, image.Width * 4);
                    var thumbnail = ShellThumbnail.Resize(ShellThumbnail.CropOpaqueRect(bitmap, isRGB: false), ThumbnailSize);
                    thumbnail?.Freeze();
                    return thumbnail;
                }
                catch
                {
                    return null;
                }
            }

            public IEnumerable<string> Resolve(ImmutableList<string> ids, ImmutableList<string> paths)
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    if (i < paths.Count && idOfPath.TryGetValue(paths[i], out var resolved))
                        yield return resolved;
                    else if (PathOf.ContainsKey(ids[i]))
                        yield return ids[i];
                }
            }
        }

        private sealed record PsdNode(string Id, string Name, string DisplayPath)
        {
            public List<PsdNode> Children { get; } = [];
        }
    }
}
