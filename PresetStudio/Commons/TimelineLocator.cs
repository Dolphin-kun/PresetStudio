using System.Reflection;
using System.Windows;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.ViewModels;

namespace PresetStudio.Commons
{
    internal static class TimelineLocator
    {
        public static Timeline? Find(DependencyObject view) => GetModelProperty(view, "Timeline") as Timeline;

        public static YukkuriMovieMaker.UndoRedo.UndoRedoManager? FindUndoRedoManager(DependencyObject view) =>
            GetModelProperty(view, "UndoRedoManager") as YukkuriMovieMaker.UndoRedo.UndoRedoManager;

        public static Scenes? FindScenes(DependencyObject view) => GetModelProperty(view, "Scenes") as Scenes;

        public static string? FindProjectPath(Scenes scenes)
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.DataContext is not IMainViewModel mainViewModel)
                        continue;

                    var model = mainViewModel.GetType().GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainViewModel);
                    if (model is null || !ReferenceEquals(model.GetType().GetProperty("Scenes")?.GetValue(model), scenes))
                        continue;

                    return model.GetType().GetProperty("ProjectFilePath")?.GetValue(model) as string;
                }
            }
            catch
            {
            }

            return null;
        }

        public static IEnumerable<Timeline> FindAllTimelines(DependencyObject view) =>
            GetModelProperty(view, "Scenes") is Scenes scenes ? scenes.Timelines : Find(view) is { } timeline ? [timeline] : [];

        private static object? GetModelProperty(DependencyObject view, string name)
        {
            try
            {
                var window = Window.GetWindow(view);
                while (window is not null && window.DataContext is not IMainViewModel)
                    window = window.Owner;

                if (window?.DataContext is not IMainViewModel mainViewModel)
                    return null;

                var model = mainViewModel.GetType().GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainViewModel);
                return model?.GetType().GetProperty(name)?.GetValue(model);
            }
            catch
            {
                return null;
            }
        }
    }
}
