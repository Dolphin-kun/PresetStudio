using System.IO;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PresetStudio.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Settings;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace PresetStudio.Model
{
    internal static class TemplateBackupStore
    {
        private const int MaxBackupCount = 10;
        private const string SettingFileName = "YukkuriMovieMaker.Settings.ItemSettings.json";

        private static readonly TimeSpan MainWindowWaitLimit = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MainWindowPollInterval = TimeSpan.FromMilliseconds(500);

        private static bool isCaptured;
        private static bool isCommitted;
        private static JArray? startupTemplates;
        private static Dictionary<string, int> notLoadedTemplates = [];

        public static IReadOnlyList<string> LostTemplateNames { get; private set; } = [];

        public static void CaptureOnStartup()
        {
            if (isCaptured)
                return;

            isCaptured = true;

            try
            {
                startupTemplates = ReadSettingTemplates();
                if (Application.Current is { } application)
                    _ = CommitAfterMainWindowShownAsync(application);
            }
            catch
            {
            }
        }

        private static async Task CommitAfterMainWindowShownAsync(Application application)
        {
            try
            {
                var limit = DateTime.UtcNow + MainWindowWaitLimit;
                while (DateTime.UtcNow < limit)
                {
                    if (await application.Dispatcher.InvokeAsync(() => FindMainWindow() is not null))
                    {
                        await application.Dispatcher.InvokeAsync(CommitStartupBackup);
                        return;
                    }

                    await Task.Delay(MainWindowPollInterval);
                }
            }
            catch
            {
            }
        }

        private static Window? FindMainWindow() =>
            ZOrderedWindowEnumerable.CreateMainWindows().FirstOrDefault(window => window is { IsVisible: true, IsLoaded: true });

        public static void CommitStartupBackup()
        {
            if (isCommitted || startupTemplates is not { } templates)
                return;

            isCommitted = true;
            CheckLoadedTemplates(templates);

            try
            {
                var settings = PresetStudioSettings.Default;
                var text = templates.ToString(Formatting.None);
                var previous = settings.TemplateBackups.FirstOrDefault();
                if (previous?.TemplatesJson == text)
                    return;

                if (previous is not null && Parse(previous.TemplatesJson) is { } previousTemplates)
                {
                    var names = GetNames(templates).ToHashSet();
                    LostTemplateNames = [.. GetNames(previousTemplates).Where(name => !names.Contains(name)).Distinct()];
                }

                settings.TemplateBackups.Insert(0, new TemplateBackup { CreatedAt = DateTime.Now, TemplatesJson = text });
                while (settings.TemplateBackups.Count > MaxBackupCount)
                    settings.TemplateBackups.RemoveAt(settings.TemplateBackups.Count - 1);

                settings.Save();
            }
            catch
            {
            }
        }

        public static JArray? ReadSettingTemplates()
        {
            foreach (var path in GetSettingFilePaths())
            {
                try
                {
                    if (JObject.Parse(File.ReadAllText(path))["Templates"] is JArray templates)
                        return templates;
                }
                catch
                {
                }
            }

            return null;
        }

        public static int CountNotLoadedTemplates(IEnumerable<ItemTemplate> currentTemplates)
        {
            var current = GetItemCounts(currentTemplates);
            return notLoadedTemplates.Count(x => !current.TryGetValue(x.Key, out var count) || count < x.Value);
        }

        private static void CheckLoadedTemplates(JArray savedTemplates)
        {
            try
            {
                var loaded = GetItemCounts(SettingsBase<ItemSettings>.Default.Templates);
                notLoadedTemplates = savedTemplates.OfType<JObject>()
                    .Where(template => !loaded.TryGetValue(GetName(template), out var count) || count < GetItemCount(template))
                    .GroupBy(GetName)
                    .ToDictionary(group => group.Key, group => group.Max(GetItemCount));
            }
            catch
            {
            }
        }

        public static IReadOnlyList<RestorableTemplate> FindRestorableTemplates(IEnumerable<ItemTemplate> currentTemplates)
        {
            var current = GetItemCounts(currentTemplates);
            var checkedNames = new HashSet<string>();
            var result = new List<RestorableTemplate>();

            foreach (var backup in PresetStudioSettings.Default.TemplateBackups)
            {
                if (Parse(backup.TemplatesJson) is not { } templates)
                    continue;

                var backupTime = backup.CreatedAt;
                foreach (var template in templates.OfType<JObject>())
                {
                    var name = GetName(template);
                    if (!checkedNames.Add(name))
                        continue;

                    var itemCount = GetItemCount(template);
                    int? currentItemCount = current.TryGetValue(name, out var count) ? count : null;
                    if (currentItemCount >= itemCount)
                        continue;

                    result.Add(new RestorableTemplate(
                        name,
                        template.ToString(Formatting.None),
                        backupTime,
                        itemCount,
                        currentItemCount,
                        GetMissingAssemblies(template)));
                }
            }

            return result;
        }

        public static ItemTemplate? CreateTemplate(RestorableTemplate template) => YmmJson.LoadFromText<ItemTemplate>(template.Json);

        private static IReadOnlyList<string> GetMissingAssemblies(JObject template) =>
            [.. template.Descendants()
                .OfType<JProperty>()
                .Where(property => property.Name == "$type")
                .Select(property => (string?)property.Value)
                .OfType<string>()
                .Distinct()
                .Where(typeName => Type.GetType(RenamedTypeResolver.ResolveTypeName(typeName)) is null)
                .Select(typeName => typeName[(typeName.LastIndexOf(',') + 1)..].Trim())
                .Distinct()];

        private static Dictionary<string, int> GetItemCounts(IEnumerable<ItemTemplate> templates) =>
            templates
                .GroupBy(template => template.Name ?? string.Empty)
                .ToDictionary(group => group.Key, group => group.Max(template => template.Items.Count));

        private static IEnumerable<string> GetNames(JArray templates) => templates.OfType<JObject>().Select(GetName);

        private static string GetName(JObject template) => (string?)template["Name"] ?? string.Empty;

        private static int GetItemCount(JObject template) => (template["Items"] as JArray)?.Count ?? 0;

        private static JArray? Parse(string json)
        {
            try
            {
                return JArray.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetSettingFilePaths()
        {
            if (!Directory.Exists(AppDirectories.SettingDirectory))
                return [];

            return Directory.GetDirectories(AppDirectories.SettingDirectory, "*.*.*.*")
                .Select(directory => (Directory: directory, Version: Version.TryParse(Path.GetFileName(directory), out var version) ? version : null))
                .Where(x => x.Version is not null && x.Version <= AppVersion.Current)
                .OrderByDescending(x => x.Version)
                .SelectMany(x => new[]
                {
                    Path.Combine(x.Directory, SettingFileName),
                    Path.Combine(x.Directory, SettingFileName + ".bak"),
                })
                .Where(File.Exists);
        }
    }
}
