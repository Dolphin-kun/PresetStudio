using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using PresetStudio.Commons;
using PresetStudio.Settings;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal sealed class PresetLinkRecord
    {
        public string ProjectPath { get; set; } = string.Empty;

        public string SceneId { get; set; } = string.Empty;

        public string ItemType { get; set; } = string.Empty;

        public int Frame { get; set; }

        public int Layer { get; set; }

        public string Text { get; set; } = string.Empty;

        public string PresetId { get; set; } = string.Empty;

        public TextStyleParts Parts { get; set; }
    }

    internal sealed record PresetLink(string PresetId, TextStyleParts Parts);

    internal static class PresetLinkStore
    {
        private static readonly ConditionalWeakTable<IItem, PresetLink> links = [];
        private static readonly ConditionalWeakTable<IItem, object> watchedItems = [];
        private static readonly ConditionalWeakTable<Timeline, StrongBox<ImmutableList<IItem>>> lastItems = [];
        private static readonly ConditionalWeakTable<Scenes, object> knownScenesTable = [];
        private static readonly List<WeakReference<Scenes>> knownScenes = [];
        private static DispatcherTimer? saveTimer;
        private static bool isPruned;

        public static event Action? LinksChanged;

        public static PresetLink? GetLink(IItem item) => links.TryGetValue(item, out var link) ? link : null;

        public static void Link(IItem item, TextStylePreset preset, TextStyleParts parts) => SetLink(item, new PresetLink(preset.Id, parts));

        public static bool ApplyPreset(IItem item, TextStylePreset preset, TextStyleParts parts)
        {
            if (!TextStyleApplier.CanApply(item, preset.Item, parts))
                return false;

            if (GetLink(item) is { } link && PresetLibrary.GetAllPresets().FirstOrDefault(candidate => candidate.Id == link.PresetId) is { } previous)
                TextStyleApplier.ReplaceOnItem(item, previous.Item, preset.Item, parts);
            else
                TextStyleApplier.ApplyToItem(item, preset.Item, parts);

            Link(item, preset, parts);
            return true;
        }

        private static void SetLink(IItem item, PresetLink link)
        {
            links.AddOrUpdate(item, link);
            Watch(item);
            PresetLibrary.EnsureIdsSaved();
            ScheduleSave();
            LinksChanged?.Invoke();
        }

        public static void CopyLink(IItem from, IItem to)
        {
            if (GetLink(from) is { } link)
                SetLink(to, link);
        }

        public static List<IItem> FindLinked(IEnumerable<Timeline> timelines, TextStylePreset preset) =>
        [
            .. timelines
                .SelectMany(timeline => timeline.Items)
                .Where(item => GetLink(item)?.PresetId == preset.Id),
        ];

        public static void EnsureScope(Scenes? scenes)
        {
            if (scenes is null || knownScenesTable.TryGetValue(scenes, out _))
                return;

            knownScenesTable.Add(scenes, new object());
            knownScenes.RemoveAll(reference => !reference.TryGetTarget(out _));
            knownScenes.Add(new WeakReference<Scenes>(scenes));

            PruneRecords();
            ((INotifyPropertyChanged)scenes).PropertyChanged += Scenes_PropertyChanged;
            foreach (var timeline in scenes.Timelines)
                AttachTimeline(timeline);

            Restore(scenes);
        }

        private static void Scenes_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is Scenes scenes && e.PropertyName == nameof(Scenes.Timelines))
            {
                foreach (var timeline in scenes.Timelines)
                    AttachTimeline(timeline);
            }
        }

        private static void AttachTimeline(Timeline timeline)
        {
            if (lastItems.TryGetValue(timeline, out _))
                return;

            lastItems.Add(timeline, new StrongBox<ImmutableList<IItem>>(timeline.Items));
            timeline.PropertyChanged += Timeline_PropertyChanged;
        }

        private static void Restore(Scenes scenes)
        {
            var path = TimelineLocator.FindProjectPath(scenes);
            if (string.IsNullOrEmpty(path))
                return;

            var records = PresetStudioSettings.Default.PresetLinks.Where(record => IsSamePath(record.ProjectPath, path)).ToList();
            if (records.Count == 0)
                return;

            var restored = false;
            foreach (var timeline in scenes.Timelines)
            {
                var sceneId = timeline.ID.ToString();
                foreach (var record in records.Where(record => record.SceneId == sceneId))
                {
                    var item = timeline.Items.FirstOrDefault(item =>
                        item.GetType().Name == record.ItemType
                        && item.Frame == record.Frame
                        && item.Layer == record.Layer
                        && GetText(item) == record.Text
                        && !links.TryGetValue(item, out _));
                    if (item is null)
                        continue;

                    links.AddOrUpdate(item, new PresetLink(record.PresetId, record.Parts));
                    Watch(item);
                    restored = true;
                }
            }

            if (restored)
                LinksChanged?.Invoke();
        }

        private static void Timeline_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not Timeline timeline || e.PropertyName != nameof(Timeline.Items) || !lastItems.TryGetValue(timeline, out var box))
                return;

            var previous = box.Value!;
            var current = timeline.Items;
            box.Value = current;

            var previousSet = previous.ToHashSet(ReferenceEqualityComparer.Instance);
            var currentSet = current.ToHashSet(ReferenceEqualityComparer.Instance);
            var removed = previous.Where(item => !currentSet.Contains(item)).ToList();
            var added = current.Where(item => !previousSet.Contains(item) && !links.TryGetValue(item, out _)).ToList();
            var inherited = false;

            foreach (var item in added)
            {
                var source = removed.FirstOrDefault(old =>
                        links.TryGetValue(old, out _)
                        && old.GetType() == item.GetType()
                        && old.Layer == item.Layer
                        && item.Frame >= old.Frame
                        && item.Frame + item.Length <= old.Frame + old.Length)
                    ?? removed.Concat(current).FirstOrDefault(other =>
                        !ReferenceEquals(other, item)
                        && links.TryGetValue(other, out _)
                        && IsCopy(other, item));

                if (source is not null && GetLink(source) is { } link)
                {
                    links.AddOrUpdate(item, link);
                    Watch(item);
                    inherited = true;
                }
            }

            ScheduleSave();
            if (inherited)
                LinksChanged?.Invoke();
        }

        private static bool IsCopy(IItem source, IItem item) => (source, item) switch
        {
            (TextItem a, TextItem b) => a.Text == b.Text && TextStyleApplier.HasSameStyle(b, a),
            (VoiceItem a, VoiceItem b) => a.Serif == b.Serif && a.Character?.Name == b.Character?.Name,
            _ => false,
        };

        private static string GetText(IItem item) => item switch
        {
            TextItem text => text.Text ?? string.Empty,
            VoiceItem voice => voice.Serif ?? string.Empty,
            _ => string.Empty,
        };

        private static void Watch(IItem item)
        {
            if (watchedItems.TryGetValue(item, out _))
                return;

            watchedItems.Add(item, new object());
            item.PropertyChanged += Item_PropertyChanged;
        }

        private static void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(IItem.Frame) or nameof(IItem.Layer) or nameof(TextItem.Text) or nameof(VoiceItem.Serif))
                ScheduleSave();
        }

        private static void ScheduleSave()
        {
            saveTimer ??= CreateTimer();
            saveTimer.Stop();
            saveTimer.Start();
        }

        private static DispatcherTimer CreateTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Save();
            };
            return timer;
        }

        private static void Save()
        {
            var records = PresetStudioSettings.Default.PresetLinks;
            var changed = false;
            foreach (var reference in knownScenes.ToList())
            {
                if (!reference.TryGetTarget(out var scenes))
                    continue;

                var path = TimelineLocator.FindProjectPath(scenes);
                if (string.IsNullOrEmpty(path))
                    continue;

                var next = scenes.Timelines
                    .SelectMany(timeline => timeline.Items.Select(item => (timeline, item)))
                    .Where(pair => links.TryGetValue(pair.item, out _))
                    .Select(pair => new PresetLinkRecord
                    {
                        ProjectPath = path,
                        SceneId = pair.timeline.ID.ToString(),
                        ItemType = pair.item.GetType().Name,
                        Frame = pair.item.Frame,
                        Layer = pair.item.Layer,
                        Text = GetText(pair.item),
                        PresetId = GetLink(pair.item)!.PresetId,
                        Parts = GetLink(pair.item)!.Parts,
                    })
                    .ToList();

                var current = records.Where(record => IsSamePath(record.ProjectPath, path)).ToList();
                if (current.Count == next.Count && current.Zip(next).All(pair => IsSameRecord(pair.First, pair.Second)))
                    continue;

                foreach (var record in current)
                    records.Remove(record);
                foreach (var record in next)
                    records.Add(record);
                changed = true;
            }

            if (changed)
                PresetStudioSettings.Default.Save();
        }

        private static void PruneRecords()
        {
            if (isPruned)
                return;

            isPruned = true;
            var records = PresetStudioSettings.Default.PresetLinks;
            var stale = records.Where(record => !File.Exists(record.ProjectPath)).ToList();
            foreach (var record in stale)
                records.Remove(record);
            if (stale.Count > 0)
                PresetStudioSettings.Default.Save();
        }

        private static bool IsSameRecord(PresetLinkRecord a, PresetLinkRecord b) =>
            a.SceneId == b.SceneId && a.ItemType == b.ItemType && a.Frame == b.Frame && a.Layer == b.Layer
            && a.Text == b.Text && a.PresetId == b.PresetId && a.Parts == b.Parts;

        private static bool IsSamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
