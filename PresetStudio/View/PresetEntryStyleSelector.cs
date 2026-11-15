using System.Windows;
using System.Windows.Controls;
using PresetStudio.Model;

namespace PresetStudio.View
{
    public sealed class PresetEntryStyleSelector : StyleSelector
    {
        public Style? FolderStyle { get; set; }

        public Style? PresetStyle { get; set; }

        public override Style? SelectStyle(object item, DependencyObject container) => item is FolderEntry ? FolderStyle : PresetStyle;
    }
}
