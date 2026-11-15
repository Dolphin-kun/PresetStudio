using System.Windows;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal sealed record DropTarget(Timeline Timeline, IItem? Item, int Frame, int Layer);

    internal sealed class PresetDragData
    {
        public const string Format = "PresetStudio.PresetDragData";

        public required Func<IItem?, bool> CanDrop { get; init; }

        public required Func<DropTarget, Task<bool>> DropAsync { get; init; }

        public DataObject ToDataObject() => new(Format, this);

        public static PresetDragData? From(IDataObject data) =>
            data.GetDataPresent(Format) ? data.GetData(Format) as PresetDragData : null;

        public static PresetDragData ForTextPreset(TextStylePreset preset) => new()
        {
            CanDrop = item => item is not null && TextStyleApplier.CanApply(item, preset.Item, TextStyleApplier.CurrentParts),
            DropAsync = target => Task.FromResult(DropTextPreset(preset, target.Item)),
        };

        private static bool DropTextPreset(TextStylePreset preset, IItem? target)
        {
            var parts = TextStyleApplier.CurrentParts;
            return target is not null && PresetLinkStore.ApplyPreset(target, preset, parts);
        }
    }
}
