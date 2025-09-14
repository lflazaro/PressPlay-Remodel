using PressPlay.Models;

namespace PressPlay.Helpers
{
    public static class TrackItemExtensions
    {
        public static double GetWidth(this ITrackItem item, int zoomLevel)
            => Constants.FramesToPixels(item.Duration.TotalFrames, zoomLevel);

        public static double GetFadeInXPosition(this ITrackItem item, int zoomLevel)
            => Constants.FramesToPixels(item.FadeInFrame, zoomLevel);

        public static double GetFadeOutXPosition(this ITrackItem item, int zoomLevel)
            => Constants.FramesToPixels(item.FadeOutFrame, zoomLevel);

        public static double GetXPosition(this ITrackItem item, int zoomLevel)
            => Constants.FramesToPixels(item.Position.TotalFrames, zoomLevel);
    }
}
