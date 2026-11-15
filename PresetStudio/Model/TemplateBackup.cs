namespace PresetStudio.Model
{
    internal sealed class TemplateBackup
    {
        public DateTime CreatedAt { get; set; }

        public string TemplatesJson { get; set; } = "[]";
    }
}
