using PressPlay.Models;

namespace PressPlay.Helpers
{
    public static class TrackItemExtensions
    {
        public static double GetWidth(this ITrackItem item, int zoomLevel)
            => item.Duration.TotalFrames * Constants.TimelinePixelsInSeparator
               / Constants.TimelineZooms[zoomLevel];

        public static double GetFadeInXPosition(this ITrackItem item, int zoomLevel)
            => item.FadeInFrame * Constants.TimelinePixelsInSeparator
               / Constants.TimelineZooms[zoomLevel];

        public static double GetFadeOutXPosition(this ITrackItem item, int zoomLevel)
            => item.FadeOutFrame * Constants.TimelinePixelsInSeparator
               / Constants.TimelineZooms[zoomLevel];
    }
}
