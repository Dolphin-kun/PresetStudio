using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using PresetStudio.Model;
using PresetStudio.Preview;
using PresetStudio.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace PresetStudio.ViewModel
{
    internal sealed class AudioEffectTabViewModel : Bindable, IDisposable
    {
        private static readonly NaturalComparer nameComparer = new();

        private readonly Func<IReadOnlyList<IItem>> getSelectedItems;
        private readonly Action recordUndo;
        private List<AudioEffectPreset>? defaultEffects;
        private IReadOnlyList<AudioEffectPreset> selectedEffects = [];
        private readonly SemaphoreSlim processing = new(1);
        private int sampleGeneration;

        public ObservableCollection<AudioEffectPreset> Effects { get; } = [];

        private AudioEffectPreset? selectedEffect;
        public AudioEffectPreset? SelectedEffect { get => selectedEffect; set => Set(ref selectedEffect, value); }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                if (Set(ref searchText, value ?? string.Empty))
                    CollectionViewSource.GetDefaultView(Effects).Refresh();
            }
        }

        public ActionCommand AddEffectCommand { get; }

        public ActionCommand AddTemplateCommand { get; }

        public ActionCommand DeleteTemplateCommand { get; }

        public ActionCommand EditTemplateCommand { get; }

        public ActionCommand PlayCommand { get; }

        public double Volume
        {
            get => PresetStudioSettings.Default.AudioPreviewVolume;
            set
            {
                if (PresetStudioSettings.Default.AudioPreviewVolume == value)
                    return;

                PresetStudioSettings.Default.AudioPreviewVolume = value;
                AudioEffectPreviewer.SetVolume(value);
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(VolumeText));
            }
        }

        public string VolumeText => string.Format(Texts.PreviewVolume, Volume.ToString("0"));

        public ActionCommand SelectSampleCommand { get; }

        public ActionCommand ResetSampleCommand { get; }

        public AudioEffectTabViewModel(Func<IReadOnlyList<IItem>> getSelectedItems, Action recordUndo)
        {
            this.getSelectedItems = getSelectedItems;
            this.recordUndo = recordUndo;

            AddEffectCommand = new ActionCommand(
                parameter => (parameter as AudioEffectPreset ?? SelectedEffect) is not null && getSelectedItems().OfType<IAudioItem>().Any(),
                parameter => AddEffect(parameter as AudioEffectPreset ?? SelectedEffect));
            AddTemplateCommand = new ActionCommand(
                _ => SelectedAudioItem is { AudioEffects.Count: > 0 },
                _ => AddTemplate());
            DeleteTemplateCommand = new ActionCommand(
                parameter => GetTargets(parameter).Any(effect => effect.IsUserTemplate),
                parameter => DeleteTemplates(GetTargets(parameter)));
            EditTemplateCommand = new ActionCommand(
                parameter => (parameter as AudioEffectPreset ?? SelectedEffect) is { IsUserTemplate: true },
                parameter => EditTemplate(parameter as AudioEffectPreset ?? SelectedEffect));

            PlayCommand = new ActionCommand(
                parameter => parameter is AudioEffectPreset,
                parameter => _ = TogglePlayAsync((AudioEffectPreset)parameter!));
            SelectSampleCommand = new ActionCommand(_ => true, _ => SelectSample());
            ResetSampleCommand = new ActionCommand(
                _ => PresetStudioSettings.Default.AudioEffectSamplePath.Length > 0,
                _ => SetSample(string.Empty));
            AudioEffectPreviewer.PlaybackChanged += AudioEffectPreviewer_PlaybackChanged;

            var view = CollectionViewSource.GetDefaultView(Effects);
            view.Filter = item => item is AudioEffectPreset effect && Matches(effect);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AudioEffectPreset.SourceLabel)));

            PresetStudioSettings.Default.AudioEffectTemplates.CollectionChanged += Templates_CollectionChanged;
            SettingsBase<EffectSettings>.Default.AudioEffectTemplate.CollectionChanged += Templates_CollectionChanged;
            Sync();
        }

        private IAudioItem? SelectedAudioItem => getSelectedItems().OfType<IAudioItem>().FirstOrDefault();

        public void SetSelected(IEnumerable<object> items)
        {
            selectedEffects = [.. items.OfType<AudioEffectPreset>()];
            CommandManager.InvalidateRequerySuggested();
        }

        private List<AudioEffectPreset> GetTargets(object? parameter)
        {
            if (parameter is AudioEffectPreset target)
                return selectedEffects.Contains(target) ? [.. selectedEffects] : [target];

            return selectedEffects.Count > 0 ? [.. selectedEffects] : SelectedEffect is { } selected ? [selected] : [];
        }

        private bool Matches(AudioEffectPreset effect) =>
            searchText.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries)
                .All(token => effect.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || effect.Template.Categories.Any(category => category.Contains(token, StringComparison.OrdinalIgnoreCase)));

        private void Templates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

        private void Template_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(EffectTemplate<>.Name) or null)
                Sync();
        }

        private void Sync()
        {
            foreach (var effect in Effects.Where(effect => effect.Source != EffectSource.Default))
                effect.Template.PropertyChanged -= Template_PropertyChanged;

            var existing = Effects.ToDictionary(effect => effect.Template);
            var selected = SelectedEffect?.Template;

            IEnumerable<(EffectTemplate<IAudioEffect> Template, EffectSource Source)> templates =
            [
                .. PresetStudioSettings.Default.AudioEffectTemplates.OrderBy(x => x.Name ?? string.Empty, nameComparer).Select(x => (x, EffectSource.User)),
                .. SettingsBase<EffectSettings>.Default.AudioEffectTemplate.OrderBy(x => x.Name ?? string.Empty, nameComparer).Select(x => (x, EffectSource.Template)),
            ];

            Effects.Clear();
            foreach (var (template, source) in templates)
            {
                template.PropertyChanged += Template_PropertyChanged;
                Effects.Add(existing.GetValueOrDefault(template) ?? new AudioEffectPreset(template, source, source == EffectSource.User ? Texts.PluginName : Texts.EffectTemplateSource));
            }

            defaultEffects ??= CreateDefaultEffects();
            foreach (var effect in defaultEffects)
                Effects.Add(effect);

            SelectedEffect = Effects.FirstOrDefault(effect => effect.Template == selected);
        }

        private static List<AudioEffectPreset> CreateDefaultEffects()
        {
            var result = new List<AudioEffectPreset>();
            try
            {
                var groups = EffectFactories.AudioEffectFactories
                    .Where(factory => !factory.IsUserEffect && !factory.IsObsolete)
                    .GroupBy(factory => factory.DefaultCategories.FirstOrDefault() is { Length: > 0 } category ? category : Texts.Other)
                    .OrderBy(group => group.Key, nameComparer);

                foreach (var group in groups)
                {
                    foreach (var factory in group.OrderBy(factory => factory.Name, nameComparer))
                    {
                        try
                        {
                            var template = new EffectTemplate<IAudioEffect>(factory.Name, [factory.Create()])
                            {
                                Categories = [group.Key],
                            };
                            result.Add(new AudioEffectPreset(template, EffectSource.Default, string.Format(Texts.DefaultCategory, group.Key)));
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private void AudioEffectPreviewer_PlaybackChanged()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            dispatcher.InvokeAsync(() =>
            {
                foreach (var effect in Effects)
                    effect.IsPlaying = ReferenceEquals(AudioEffectPreviewer.PlayingKey, effect);
            });
        }

        private async Task<float[]?> ProcessAsync(AudioEffectPreset preset)
        {
            var effects = preset.Template.CreateEffects().ToList();
            await processing.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        return AudioEffectPreviewer.Process(effects, AudioEffectPreviewer.GetSample());
                    }
                    catch
                    {
                        return null;
                    }
                });
            }
            finally
            {
                processing.Release();
            }
        }

        private async Task TogglePlayAsync(AudioEffectPreset preset)
        {
            if (ReferenceEquals(AudioEffectPreviewer.PlayingKey, preset))
            {
                AudioEffectPreviewer.Stop();
                return;
            }

            AudioEffectPreviewer.Stop();
            if (await ProcessAsync(preset) is not { Length: > 0 } samples)
                return;

            preset.Peaks ??= AudioEffectPreviewer.GetPeaks(samples, PeakColumns);
            try
            {
                AudioEffectPreviewer.Play(preset, samples, AudioEffectPreviewer.GetSample().Hz);
            }
            catch
            {
            }
        }

        private const int PeakColumns = 160;

        public async void RequestWaveform(AudioEffectPreset preset)
        {
            if (preset.Peaks is not null)
                return;

            var generation = sampleGeneration;
            var samples = await ProcessAsync(preset);
            if (generation == sampleGeneration && samples is not null && preset.Peaks is null)
                preset.Peaks = AudioEffectPreviewer.GetPeaks(samples, PeakColumns);
        }

        private void SelectSample()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Texts.SelectPreviewAudio,
                Filter = Texts.AudioFileFilter,
            };
            if (dialog.ShowDialog() == true)
                SetSample(dialog.FileName);
        }

        private void SetSample(string path)
        {
            AudioEffectPreviewer.Stop();
            PresetStudioSettings.Default.AudioEffectSamplePath = path;
            PresetStudioSettings.Default.Save();
            sampleGeneration++;
            var shown = Effects.Where(effect => effect.Peaks is not null).ToList();
            foreach (var effect in Effects)
                effect.Peaks = null;
            foreach (var effect in shown)
                RequestWaveform(effect);
        }

        public static bool CanDrop(IItem? target) => target is IAudioItem;

        public static bool AddTo(AudioEffectPreset preset, IItem? target)
        {
            if (target is not IAudioItem item)
                return false;

            item.AudioEffects = item.AudioEffects.AddRange(preset.Template.CreateEffects());
            return true;
        }

        private void AddEffect(AudioEffectPreset? preset)
        {
            if (preset is null)
                return;

            var applied = false;
            foreach (var item in getSelectedItems())
                applied |= AddTo(preset, item);

            if (applied)
                recordUndo();
        }

        private void AddTemplate()
        {
            if (SelectedAudioItem is not { AudioEffects.Count: > 0 } item)
                return;

            var templates = PresetStudioSettings.Default.AudioEffectTemplates;
            var names = templates.Select(template => (template.Name ?? string.Empty, string.Empty));
            if (NameInputViewModel.Show(Texts.AddAudioEffectTemplate, Texts.TemplateName, names) is not { Name: var name })
                return;

            if (templates.FirstOrDefault(template => template.Name == name) is { } existing)
            {
                if (!PresetLibrary.Confirm(string.Format(Texts.ConfirmOverwrite, name)))
                    return;

                templates.Remove(existing);
            }

            templates.Add(new EffectTemplate<IAudioEffect>(name, item.AudioEffects));
            SelectedEffect = Effects.FirstOrDefault(effect => effect.IsUserTemplate && effect.Template.Name == name);
            PresetStudioSettings.Default.Save();
        }

        private static void EditTemplate(AudioEffectPreset? preset)
        {
            if (preset is not { IsUserTemplate: true } || NameInputViewModel.Show(Texts.EditAudioEffectTemplate, Texts.TemplateName, [], initialName: preset.Name) is not { Name: var name })
                return;

            preset.Template.Name = name;
            PresetStudioSettings.Default.Save();
        }

        private static void DeleteTemplates(IReadOnlyList<AudioEffectPreset> effects)
        {
            var targets = effects.Where(effect => effect.IsUserTemplate).ToList();
            if (targets.Count == 0)
                return;

            var message = targets.Count == 1 ? string.Format(Texts.ConfirmDelete, targets[0].Name) : string.Format(Texts.ConfirmDeleteCount, targets.Count);
            if (!PresetLibrary.Confirm(message))
                return;

            foreach (var effect in targets)
                PresetStudioSettings.Default.AudioEffectTemplates.Remove(effect.Template);
            PresetStudioSettings.Default.Save();
        }

        public void Dispose()
        {
            AudioEffectPreviewer.PlaybackChanged -= AudioEffectPreviewer_PlaybackChanged;
            AudioEffectPreviewer.Stop();
            PresetStudioSettings.Default.AudioEffectTemplates.CollectionChanged -= Templates_CollectionChanged;
            SettingsBase<EffectSettings>.Default.AudioEffectTemplate.CollectionChanged -= Templates_CollectionChanged;
            foreach (var effect in Effects.Where(effect => effect.Source != EffectSource.Default))
                effect.Template.PropertyChanged -= Template_PropertyChanged;
        }
    }
}
