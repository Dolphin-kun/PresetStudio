using System.Collections.ObjectModel;
using PresetStudio.Model;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Settings;

namespace PresetStudio.Settings
{
    internal class PresetStudioSettings : SettingsBase<PresetStudioSettings>
    {
        public override SettingsCategory Category => SettingsCategory.None;
        public override string Name => "PresetStudio";

        public override bool HasSettingView => false;
        public override object? SettingView => null;

        public ObservableCollection<TextStylePreset> Presets { get; } = [];

        public ObservableCollection<string> PresetFolders { get; } = [];

        private double audioPreviewVolume = 40;
        public double AudioPreviewVolume { get => audioPreviewVolume; set => Set(ref audioPreviewVolume, Math.Clamp(value, 0, 100)); }

        private string audioEffectSamplePath = string.Empty;
        public string AudioEffectSamplePath { get => audioEffectSamplePath; set => Set(ref audioEffectSamplePath, value ?? string.Empty); }

        private string effectPreviewImagePath = string.Empty;
        public string EffectPreviewImagePath { get => effectPreviewImagePath; set => Set(ref effectPreviewImagePath, value ?? string.Empty); }

        private string previewText = "あア";
        public string PreviewText { get => previewText; set => Set(ref previewText, string.IsNullOrEmpty(value) ? "あア" : value); }

        public ObservableCollection<FontTagRecord> FontTags { get; } = [];

        private string fontSampleText = "あいうえお アイウエオ 漢字 Abc 123";
        public string FontSampleText { get => fontSampleText; set => Set(ref fontSampleText, value ?? string.Empty); }

        private bool keepFont = true;
        public bool KeepFont { get => keepFont; set => Set(ref keepFont, value); }

        private bool keepFontSize = true;
        public bool KeepFontSize { get => keepFontSize; set => Set(ref keepFontSize, value); }

        private bool itemEditorShowFolders;
        public bool ItemEditorShowFolders { get => itemEditorShowFolders; set => Set(ref itemEditorShowFolders, value); }

        private string itemEditorFolder = string.Empty;
        public string ItemEditorFolder { get => itemEditorFolder; set => Set(ref itemEditorFolder, value ?? string.Empty); }

        private bool isPreviewOnly;
        public bool IsPreviewOnly { get => isPreviewOnly; set => Set(ref isPreviewOnly, value); }

        private string transitionBeforeImagePath = string.Empty;
        public string TransitionBeforeImagePath { get => transitionBeforeImagePath; set => Set(ref transitionBeforeImagePath, value ?? string.Empty); }

        private string transitionAfterImagePath = string.Empty;
        public string TransitionAfterImagePath { get => transitionAfterImagePath; set => Set(ref transitionAfterImagePath, value ?? string.Empty); }

        public ObservableCollection<string> FullCanvasTemplates { get; } = [];

        public ObservableCollection<TemplateFieldSet> TemplateFields { get; } = [];

        public ObservableCollection<PresetLinkRecord> PresetLinks { get; } = [];

        public ObservableCollection<string> HiddenTabs { get; } = [];

        private double thumbnailScale = 1;
        public double ThumbnailScale { get => thumbnailScale; set => Set(ref thumbnailScale, value); }

        public ObservableCollection<EffectTemplate<IVideoEffect>> EffectTemplates { get; } = [];

        public ObservableCollection<EffectTemplate<IAudioEffect>> AudioEffectTemplates { get; } = [];

        public ObservableCollection<TemplateBackup> TemplateBackups { get; } = [];

        public ObservableCollection<RuleImageRecord> RuleImages { get; } = [];

        public ObservableCollection<FacePreset> FacePresets { get; } = [];

        public ObservableCollection<TachieItemPreset> TachiePresets { get; } = [];

        private string tachieCharacterName = string.Empty;
        public string TachieCharacterName { get => tachieCharacterName; set => Set(ref tachieCharacterName, value ?? string.Empty); }

        private TextSplitMode splitMode = TextSplitMode.Selection;
        public TextSplitMode SplitMode { get => splitMode; set => Set(ref splitMode, value); }

        private int splitInterval;
        public int SplitInterval { get => splitInterval; set => Set(ref splitInterval, Math.Max(0, value)); }

        public override void Initialize()
        {
        }
    }
}
