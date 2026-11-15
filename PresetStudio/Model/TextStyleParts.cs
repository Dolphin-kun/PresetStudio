namespace PresetStudio.Model
{
    [Flags]
    internal enum TextStyleParts
    {
        None = 0,
        Font = 1 << 0,
        FontSize = 1 << 1,
        Color = 1 << 2,
        Decoration = 1 << 3,
        Format = 1 << 4,
        Layout = 1 << 5,
        Animation = 1 << 6,
        Effects = 1 << 7,
        All = Font | FontSize | Color | Decoration | Format | Layout | Animation | Effects,
    }
}
