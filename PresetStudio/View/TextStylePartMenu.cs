using System.Windows;
using System.Windows.Controls;
using PresetStudio.Model;

namespace PresetStudio.View
{
    internal static class TextStylePartMenu
    {
        private static readonly (TextStyleParts Parts, string Label)[] items =
        [
            (TextStyleParts.Font, Texts.PartFont),
            (TextStyleParts.FontSize, Texts.PartFontSize),
            (TextStyleParts.Color, Texts.PartColor),
            (TextStyleParts.Decoration, Texts.PartDecoration),
            (TextStyleParts.Format, Texts.PartFormat),
            (TextStyleParts.Layout, Texts.PartLayout),
            (TextStyleParts.Animation, Texts.PartAnimation),
            (TextStyleParts.Effects, Texts.PartEffects),
        ];

        public static void Fill(MenuItem parent, RoutedEventHandler click)
        {
            if (parent.Items.Count > 0)
                return;

            foreach (var (parts, label) in items)
            {
                var item = new MenuItem { Header = label, Tag = parts };
                item.Click += click;
                parent.Items.Add(item);
            }
        }

        public static TextStylePreset? FindPreset(object sender)
        {
            for (var element = sender as DependencyObject; element is not null; element = LogicalTreeHelper.GetParent(element))
            {
                if (element is ContextMenu { PlacementTarget: FrameworkElement { DataContext: TextStylePreset preset } })
                    return preset;
            }

            return null;
        }
    }
}
