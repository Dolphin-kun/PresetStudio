using YukkuriMovieMaker.Project;

namespace PresetStudio.Preview
{
    internal sealed record PreviewVideoInfo(int Width, int Height, int FPS, int Hz)
    {
        public static PreviewVideoInfo From(VideoInfo videoInfo) => new(videoInfo.Width, videoInfo.Height, videoInfo.FPS, videoInfo.Hz);
    }
}
