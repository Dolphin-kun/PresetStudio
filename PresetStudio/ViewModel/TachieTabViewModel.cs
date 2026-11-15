using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using PresetStudio.Model;
using PresetStudio.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace PresetStudio.ViewModel
{
    internal sealed class TachieTabViewModel : Bindable
    {
        private readonly Func<Timeline?> getTimeline;
        private readonly Func<IReadOnlyList<IItem>> getSelectedItems;
        private readonly Action recordUndo;
        private readonly Dictionary<string, FacePreset> defaultFaces = [];
        private IReadOnlyList<IPreviewItem> selectedEntries = [];

        public ObservableCollection<Character> Characters { get; } = [];

        public ObservableCollection<IPreviewItem> Entries { get; } = [];

        private Character? selectedCharacter;
        public Character? SelectedCharacter
        {
            get => selectedCharacter;
            set
            {
                if (!Set(ref selectedCharacter, value, nameof(SelectedCharacter), nameof(HasNoTachie)))
                    return;

                if (value is not null)
                    PresetStudioSettings.Default.TachieCharacterName = value.Name;
                RebuildEntries();
            }
        }

        public bool HasNoTachie => selectedCharacter is { } character && !CharacterProvider.HasTachie(character);

        private IPreviewItem? selectedEntry;
        public IPreviewItem? SelectedEntry
        {
            get => selectedEntry;
            set
            {
                if (!Set(ref selectedEntry, value))
                    return;

                BeginEdit(value as FacePreset);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private FacePreset? editingSource;

        private ITachieFaceParameter? editingFace;
        public ITachieFaceParameter? EditingFace
        {
            get => editingFace;
            private set
            {
                Set(ref editingFace, value, nameof(EditingFace), nameof(IsEditing), nameof(ShowsPropertyEditor));
            }
        }

        public bool IsEditing => editingFace is not null;

        public ObservableCollection<FacePartOption> EditingParts { get; } = [];

        private bool hasParts;
        public bool HasParts { get => hasParts; private set => Set(ref hasParts, value, nameof(HasParts), nameof(ShowsPropertyEditor)); }

        public bool ShowsPropertyEditor => IsEditing && !hasParts;

        private int partsGeneration;

        public ActionCommand SaveEditingAsNewCommand { get; }

        public ActionCommand OverwriteEditingCommand { get; }

        public ActionCommand AddCommand { get; }

        public ActionCommand DeleteCommand { get; }

        public ActionCommand RenameCommand { get; }

        public ActionCommand ApplyCommand { get; }

        private static PresetStudioSettings Settings => PresetStudioSettings.Default;

        public TachieTabViewModel(Func<Timeline?> getTimeline, Func<IReadOnlyList<IItem>> getSelectedItems, Action recordUndo)
        {
            this.getTimeline = getTimeline;
            this.getSelectedItems = getSelectedItems;
            this.recordUndo = recordUndo;

            SaveEditingAsNewCommand = new ActionCommand(_ => IsEditing, _ => SaveEditingAsNew());
            OverwriteEditingCommand = new ActionCommand(_ => IsEditing && editingSource is { IsDefault: false }, _ => OverwriteEditing());

            AddCommand = new ActionCommand(_ => GetSourceItem() is not null, _ => Add());
            DeleteCommand = new ActionCommand(
                parameter => GetTargets(parameter).Any(IsDeletable),
                parameter => Delete(GetTargets(parameter)));
            RenameCommand = new ActionCommand(
                parameter => GetTargets(parameter) is [var entry] && IsDeletable(entry),
                parameter => Rename(GetTargets(parameter)[0]));
            ApplyCommand = new ActionCommand(
                parameter => (parameter as IPreviewItem ?? SelectedEntry) is FacePreset or TachieItemPreset && getTimeline() is not null,
                parameter => Apply(parameter as IPreviewItem ?? SelectedEntry));

            CollectionViewSource.GetDefaultView(Entries).GroupDescriptions.Add(new PropertyGroupDescription(nameof(FacePreset.GroupLabel)));
            CollectionViewSource.GetDefaultView(EditingParts).GroupDescriptions.Add(new PropertyGroupDescription(nameof(FacePartOption.Group)));
        }

        private void BeginEdit(FacePreset? face)
        {
            editingSource = face;
            EditingFace = face?.FaceParameter is { } parameter ? YmmJson.GetClone(parameter) : null;
            _ = LoadPartsAsync();
        }

        private async Task LoadPartsAsync()
        {
            var generation = ++partsGeneration;
            EditingParts.Clear();
            HasParts = false;
            if (editingSource is not { } source || editingFace is not { } face || CharacterProvider.Find(source.CharacterName, getTimeline()) is not { } character)
                return;

            var dispatcher = Dispatcher.CurrentDispatcher;
            var options = await FacePartProvider.CreateOptionsAsync(character, face);
            await dispatcher.InvokeAsync(() =>
            {
                if (generation != partsGeneration)
                    return;

                foreach (var option in options)
                    EditingParts.Add(option);
                HasParts = options.Count > 0;
            });

            foreach (var option in options)
            {
                if (generation != partsGeneration)
                    return;

                await option.LoadImageAsync();
            }
        }

        public void SelectPart(FacePartOption option)
        {
            if (editingFace is null || option.Apply(editingFace) is not { } face)
                return;

            EditingFace = face;
            foreach (var part in EditingParts)
                part.UpdateCurrent(face);
            ApplyEditingToSelection();
        }

        public void ApplyEditingToSelection()
        {
            if (getTimeline() is not { } timeline || editingSource is null || editingFace is null)
                return;

            if (ApplyFaceToSelection(timeline, CreateEditingPreset(editingSource.Name)))
                recordUndo();
        }

        private FacePreset CreateEditingPreset(string name) => new()
        {
            Name = name,
            CharacterName = editingSource?.CharacterName ?? string.Empty,
            FaceParameter = editingFace is null ? null : YmmJson.GetClone(editingFace),
            FaceEffects = CloneEffects(editingSource?.FaceEffects ?? []),
        };

        private void SaveEditingAsNew()
        {
            var names = Settings.FacePresets.Select(x => x.Name).Distinct().Select(x => (x, string.Empty));
            if (NameInputViewModel.Show(Texts.SaveAsNewFace, Texts.NameLabel, names) is not { Name: var name })
                return;

            var preset = CreateEditingPreset(name);
            Settings.FacePresets.Add(preset);
            Settings.Save();
            RebuildEntries();
            SelectedEntry = preset;
        }

        private void OverwriteEditing()
        {
            if (editingSource is not { IsDefault: false } source || editingFace is null)
                return;

            if (!PresetLibrary.Confirm(string.Format(Texts.ConfirmOverwrite, source.Name)))
                return;

            source.FaceParameter = YmmJson.GetClone(editingFace);
            source.PreviewImage = null;
            Settings.Save();
            RebuildEntries();
        }

        private static bool IsDeletable(IPreviewItem entry) => entry is FacePreset { IsDefault: false } or TachieItemPreset;

        public void SetSelected(IEnumerable<object> items)
        {
            selectedEntries = [.. items.OfType<IPreviewItem>()];
            CommandManager.InvalidateRequerySuggested();
        }

        private List<IPreviewItem> GetTargets(object? parameter)
        {
            if (parameter is IPreviewItem target)
                return selectedEntries.Contains(target) ? [.. selectedEntries] : [target];

            if (selectedEntries.Count > 0)
                return [.. selectedEntries];

            return SelectedEntry is null ? [] : [SelectedEntry];
        }

        public void RefreshCharacters()
        {
            var characters = CharacterProvider.GetCharacters(getTimeline()).ToList();
            if (characters.SequenceEqual(Characters))
                return;

            var name = selectedCharacter?.Name ?? Settings.TachieCharacterName;
            Characters.Clear();
            foreach (var character in characters)
                Characters.Add(character);

            SelectedCharacter = Characters.FirstOrDefault(character => character.Name == name)
                ?? Characters.FirstOrDefault(CharacterProvider.HasTachie)
                ?? Characters.FirstOrDefault();
        }

        public void RebuildEntries()
        {
            Entries.Clear();
            if (selectedCharacter is not { } character)
                return;

            if (CharacterProvider.HasTachie(character))
                Entries.Add(GetDefaultFace(character));
            foreach (var face in Settings.FacePresets.Where(face => face.CharacterName == character.Name))
                Entries.Add(face);

            foreach (var tachie in Settings.TachiePresets.Where(tachie => tachie.CharacterName == character.Name))
                Entries.Add(tachie);
        }

        private FacePreset GetDefaultFace(Character character)
        {
            if (defaultFaces.TryGetValue(character.Name, out var cached) && ReferenceEquals(cached.FaceParameter, character.TachieDefaultFaceParameter))
                return cached;

            var face = new FacePreset
            {
                Name = Texts.DefaultFace,
                CharacterName = character.Name,
                FaceParameter = character.TachieDefaultFaceParameter,
                FaceEffects = character.TachieDefaultFaceEffects,
                IsDefault = true,
            };
            defaultFaces[character.Name] = face;
            return face;
        }

        private IItem? GetSourceItem() => getSelectedItems().FirstOrDefault(item => item switch
        {
            TachieFaceItem { Character: not null, TachieFaceParameter: not null } => true,
            VoiceItem { Character: not null, TachieFaceParameter: not null } => true,
            TachieItem { Character: not null } => true,
            _ => false,
        });

        private void Add()
        {
            if (GetSourceItem() is not { } item)
                return;

            var isTachie = item is TachieItem;
            var title = isTachie ? Texts.SaveTachieItem : Texts.SaveFace;
            var names = (isTachie ? Settings.TachiePresets.Select(x => x.Name) : Settings.FacePresets.Select(x => x.Name)).Distinct().Select(x => (x, string.Empty));
            if (NameInputViewModel.Show(title, Texts.NameLabel, names) is not { Name: var name })
                return;

            Character character;
            switch (item)
            {
                case TachieItem tachie:
                    character = tachie.Character;
                    var clone = (TachieItem)tachie.GetClone();
                    clone.Frame = 0;
                    clone.Layer = 0;
                    Settings.TachiePresets.Add(new TachieItemPreset { Name = name, CharacterName = character.Name, Item = clone });
                    break;
                case TachieFaceItem face:
                    character = face.Character;
                    Settings.FacePresets.Add(CreateFacePreset(name, character, face.TachieFaceParameter, face.TachieFaceEffects));
                    break;
                case VoiceItem voice:
                    character = voice.Character;
                    Settings.FacePresets.Add(CreateFacePreset(name, character, voice.TachieFaceParameter, voice.TachieFaceEffects));
                    break;
                default:
                    return;
            }

            Settings.Save();
            if (selectedCharacter?.Name != character.Name)
                SelectedCharacter = Characters.FirstOrDefault(x => x.Name == character.Name) ?? selectedCharacter;
            RebuildEntries();
        }

        private static FacePreset CreateFacePreset(string name, Character character, ITachieFaceParameter parameter, ImmutableList<IVideoEffect> effects) => new()
        {
            Name = name,
            CharacterName = character.Name,
            FaceParameter = YmmJson.GetClone(parameter),
            FaceEffects = CloneEffects(effects),
        };

        private static ImmutableList<IVideoEffect> CloneEffects(IEnumerable<IVideoEffect> effects) =>
            [.. effects.Select(effect => YmmJson.GetClone(effect)).OfType<IVideoEffect>()];

        private void Delete(IReadOnlyList<IPreviewItem> entries)
        {
            var targets = entries.Where(IsDeletable).ToList();
            if (targets.Count == 0)
                return;

            var message = targets.Count == 1 ? string.Format(Texts.ConfirmDelete, GetName(targets[0])) : string.Format(Texts.ConfirmDeleteCount, targets.Count);
            if (!PresetLibrary.Confirm(message))
                return;

            foreach (var target in targets)
            {
                switch (target)
                {
                    case FacePreset face:
                        Settings.FacePresets.Remove(face);
                        break;
                    case TachieItemPreset tachie:
                        Settings.TachiePresets.Remove(tachie);
                        break;
                }
            }

            Settings.Save();
            RebuildEntries();
        }

        private static void Rename(IPreviewItem entry)
        {
            if (NameInputViewModel.Show(Texts.Rename, Texts.NameLabel, [], initialName: GetName(entry)) is not { Name: var name })
                return;

            switch (entry)
            {
                case FacePreset face:
                    face.Name = name;
                    break;
                case TachieItemPreset tachie:
                    tachie.Name = name;
                    break;
            }

            Settings.Save();
        }

        private static string GetName(IPreviewItem entry) => entry switch
        {
            FacePreset face => face.Name,
            TachieItemPreset tachie => tachie.Name,
            _ => string.Empty,
        };

        private void Apply(IPreviewItem? entry)
        {
            if (getTimeline() is not { } timeline)
                return;

            if (ApplyTo(entry, timeline, getSelectedItems(), null))
                recordUndo();
        }

        public static bool CanDrop(IPreviewItem entry, IItem? target) => (entry, target) switch
        {
            (_, null) => true,
            (FacePreset face, TachieFaceItem item) => item.Character?.Name == face.CharacterName,
            (FacePreset face, VoiceItem item) => item.Character?.Name == face.CharacterName,
            (FacePreset face, TachieItem item) => item.Character?.Name == face.CharacterName,
            (TachieItemPreset tachie, TachieItem item) => item.Character?.Name == tachie.CharacterName,
            _ => false,
        };

        public static bool Drop(IPreviewItem entry, Timeline timeline, IItem? target, int frame, int layer) =>
            ApplyTo(entry, timeline, target is null ? [] : [target], (frame, layer));

        private static bool ApplyTo(IPreviewItem? entry, Timeline timeline, IReadOnlyList<IItem> items, (int Frame, int Layer)? placement) => entry switch
        {
            FacePreset face => ApplyFace(timeline, face, items, placement),
            TachieItemPreset tachie => ApplyTachie(timeline, tachie, items, placement),
            _ => false,
        };

        private static bool ApplyFace(Timeline timeline, FacePreset preset, IReadOnlyList<IItem> items, (int Frame, int Layer)? placement)
        {
            if (ApplyFaceToItems(timeline, preset, items))
                return true;

            if (preset.FaceParameter is not { } parameter || CharacterProvider.Find(preset.CharacterName, timeline) is not { } character)
                return false;

            var newFace = new TachieFaceItem(character)
            {
                TachieFaceParameter = YmmJson.GetClone(parameter)!,
                TachieFaceEffects = CloneEffects(preset.FaceEffects),
            };
            return AddItem(timeline, newFace, placement);
        }

        private bool ApplyFaceToSelection(Timeline timeline, FacePreset preset) => ApplyFaceToItems(timeline, preset, getSelectedItems());

        private static bool ApplyFaceToItems(Timeline timeline, FacePreset preset, IReadOnlyList<IItem> items)
        {
            if (preset.FaceParameter is not { } parameter || CharacterProvider.Find(preset.CharacterName, timeline) is not { } character)
                return false;

            var applied = false;
            foreach (var item in items)
            {
                switch (item)
                {
                    case TachieFaceItem face when face.Character?.Name == character.Name:
                        face.TachieFaceParameter = YmmJson.GetClone(parameter)!;
                        face.TachieFaceEffects = CloneEffects(preset.FaceEffects);
                        applied = true;
                        break;
                    case VoiceItem voice when voice.Character?.Name == character.Name:
                        voice.TachieFaceParameter = YmmJson.GetClone(parameter)!;
                        voice.TachieFaceEffects = CloneEffects(preset.FaceEffects);
                        applied = true;
                        break;
                    case TachieItem tachie when tachie.Character?.Name == character.Name:
                        applied |= ApplyFaceToTachie(timeline, tachie, character, parameter, preset.FaceEffects);
                        break;
                }
            }

            return applied;
        }

        private static bool ApplyFaceToTachie(Timeline timeline, TachieItem tachie, Character character, ITachieFaceParameter parameter, IEnumerable<IVideoEffect> effects)
        {
            var frame = tachie.Frame <= timeline.CurrentFrame && timeline.CurrentFrame < tachie.Frame + tachie.Length ? timeline.CurrentFrame : tachie.Frame;
            var active = timeline.Items
                .Where(item => item.Frame <= frame && frame < item.Frame + item.Length)
                .Where(item => item is TachieFaceItem face && face.Character?.Name == character.Name || item is VoiceItem voice && voice.Character?.Name == character.Name)
                .OrderByDescending(item => item.Layer)
                .FirstOrDefault();

            switch (active)
            {
                case TachieFaceItem face:
                    face.TachieFaceParameter = YmmJson.GetClone(parameter)!;
                    face.TachieFaceEffects = CloneEffects(effects);
                    return true;
                case VoiceItem voice:
                    voice.TachieFaceParameter = YmmJson.GetClone(parameter)!;
                    voice.TachieFaceEffects = CloneEffects(effects);
                    return true;
            }

            var newFace = new TachieFaceItem(character)
            {
                Length = tachie.Length,
                TachieFaceParameter = YmmJson.GetClone(parameter)!,
                TachieFaceEffects = CloneEffects(effects),
            };
            return timeline.TryAddItems([newFace], tachie.Frame, tachie.Layer + 1, false);
        }

        private static bool ApplyTachie(Timeline timeline, TachieItemPreset preset, IReadOnlyList<IItem> items, (int Frame, int Layer)? placement)
        {
            if (CharacterProvider.Find(preset.CharacterName, timeline) is not { } character)
                return false;

            var applied = false;
            foreach (var target in items.OfType<TachieItem>().Where(item => item.Character?.Name == character.Name))
            {
                var source = preset.Item;
                target.TachieItemParameter = YmmJson.GetClone(source.TachieItemParameter)!;
                target.X.CopyFrom(source.X);
                target.Y.CopyFrom(source.Y);
                target.Z.CopyFrom(source.Z);
                target.Opacity.CopyFrom(source.Opacity);
                target.Zoom.CopyFrom(source.Zoom);
                target.Rotation.CopyFrom(source.Rotation);
                target.VideoEffects = CloneEffects(source.VideoEffects);
                applied = true;
            }

            if (applied)
                return true;

            var clone = (TachieItem)preset.Item.GetClone();
            clone.Character = character;
            return AddItem(timeline, clone, placement);
        }

        private static bool AddItem(Timeline timeline, IItem item, (int Frame, int Layer)? placement)
        {
            if (placement is { } position)
                return timeline.TryAddItems([item], position.Frame, position.Layer);

            var frame = timeline.CurrentFrame;
            var layer = timeline.Items.Where(x => x.Frame <= frame && frame < x.Frame + x.Length).Select(x => x.Layer + 1).DefaultIfEmpty(0).Max();
            return timeline.TryAddItems([item], frame, layer);
        }
    }
}
