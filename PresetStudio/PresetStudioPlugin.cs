using System.Reflection;
using PresetStudio.Commons;
using PresetStudio.Model;
using PresetStudio.View;
using PresetStudio.ViewModel;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Update;

namespace PresetStudio
{
    [PluginDetails(AuthorName = "いるかぁぁ")]
    public class PresetStudioPlugin : IToolPlugin
    {
        public string Name => Texts.PluginName;
        public Type ViewModelType => typeof(PresetStudioViewModel);
        public Type ViewType => typeof(PresetStudioView);

        public PluginDetailsAttribute Details => GetType().GetCustomAttribute<PluginDetailsAttribute>() ?? new();
        public IPluginUpdater? Updater { get; } = new Ymm4InfoPluginUpdater<PresetStudioPlugin>("3161829460292123");

        public PresetStudioPlugin()
        {
            TemplateBackupStore.CaptureOnStartup();
            ItemEditorPresetHook.Register();
            TimelineDropHandler.Register();
        }
    }
}
