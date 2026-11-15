using YukkuriMovieMaker.Commons;

namespace PresetStudio.Model
{
    internal sealed class RestorableTemplate : Bindable
    {
        public string Name { get; }

        public string Json { get; }

        public DateTime BackupTime { get; }

        public int ItemCount { get; }

        public int? CurrentItemCount { get; }

        public IReadOnlyList<string> MissingAssemblies { get; }

        public bool CanRestore => MissingAssemblies.Count == 0;

        private bool isChecked;
        public bool IsChecked { get => isChecked; set => Set(ref isChecked, value); }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Texts.Unnamed : Name;

        public string Description
        {
            get
            {
                var current = CurrentItemCount is { } count ? string.Format(Texts.BackupCurrentItems, count) : Texts.BackupCurrentNone;
                var description = string.Format(Texts.BackupDescription, BackupTime.ToString("yyyy/MM/dd HH:mm"), ItemCount, current);
                return CanRestore
                    ? description
                    : $"{description}\n{string.Format(Texts.BackupMissingPlugins, string.Join(", ", MissingAssemblies))}";
            }
        }

        public RestorableTemplate(
            string name,
            string json,
            DateTime backupTime,
            int itemCount,
            int? currentItemCount,
            IReadOnlyList<string> missingAssemblies)
        {
            Name = name;
            Json = json;
            BackupTime = backupTime;
            ItemCount = itemCount;
            CurrentItemCount = currentItemCount;
            MissingAssemblies = missingAssemblies;
            isChecked = CanRestore;
        }
    }
}
