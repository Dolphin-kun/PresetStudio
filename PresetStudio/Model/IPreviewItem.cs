using System.Windows.Media.Imaging;

namespace PresetStudio.Model
{
    internal interface IPreviewItem
    {
        BitmapSource? PreviewImage { get; set; }
    }
}
