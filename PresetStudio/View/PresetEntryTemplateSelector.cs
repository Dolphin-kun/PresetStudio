using System.Windows;
using System.Windows.Controls;
using PresetStudio.Model;

namespace PresetStudio.View
{
    public sealed class PresetEntryTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? FolderTemplate { get; set; }

        public DataTemplate? PresetTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is FolderEntry ? FolderTemplate : PresetTemplate;
    }
}
