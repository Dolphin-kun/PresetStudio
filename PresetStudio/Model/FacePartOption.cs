using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Tachie;

namespace PresetStudio.Model
{
    internal sealed class FacePartOption : Bindable, IPreviewItem
    {
        private readonly Func<ITachieFaceParameter, ITachieFaceParameter?> apply;
        private readonly Func<ITachieFaceParameter, bool> isSelected;
        private readonly Func<Task<BitmapSource?>>? loadImage;

        public string Group { get; }

        public string Name { get; }

        private bool isCurrent;
        public bool IsCurrent { get => isCurrent; private set => Set(ref isCurrent, value); }

        private BitmapSource? previewImage;
        public BitmapSource? PreviewImage { get => previewImage; set => Set(ref previewImage, value); }

        public FacePartOption(
            string group,
            string name,
            ITachieFaceParameter currentFace,
            Func<ITachieFaceParameter, ITachieFaceParameter?> apply,
            Func<ITachieFaceParameter, bool> isSelected,
            BitmapSource? image = null,
            Func<Task<BitmapSource?>>? loadImage = null)
        {
            Group = group;
            Name = name;
            this.apply = apply;
            this.isSelected = isSelected;
            this.loadImage = loadImage;
            previewImage = image;
            UpdateCurrent(currentFace);
        }

        public ITachieFaceParameter? Apply(ITachieFaceParameter face) => apply(face);

        public void UpdateCurrent(ITachieFaceParameter face) => IsCurrent = isSelected(face);

        public async Task LoadImageAsync()
        {
            if (previewImage is not null || loadImage is null)
                return;

            try
            {
                PreviewImage = await loadImage();
            }
            catch
            {
            }
        }
    }
}
