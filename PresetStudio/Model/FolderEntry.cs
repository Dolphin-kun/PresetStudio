namespace PresetStudio.Model
{
    internal sealed class FolderEntry(string name, string path)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;
    }
}
