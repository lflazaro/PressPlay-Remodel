using System.Collections.Generic;

namespace PressPlay.Helpers
{
    public static class Constants
    {
        // Pixels per frame at zoom level 1
        public static double TimelinePixelsInSeparator = 0.2;
        public const double TrackHeight = 60;   // whatever looks good

        // Zoom levels with multipliers
        public static Dictionary<int, double> TimelineZooms = new Dictionary<int, double>
        {
            { 1, 0.1 },   // 2.0 pixels per frame
            { 2, 0.3 },   // 0.7 pixels per frame
            { 3, 0.5 },   // 0.4 pixels per frame
            { 4, 1.0 },   // 0.2 pixels per frame
            { 5, 2.0 },   // 0.1 pixels per frame
            { 6, 3.0 },   // 0.07 pixels per frame
            { 7, 4.0 },   // 0.05 pixels per frame
            { 8, 6.0 },   // 0.03 pixels per frame
            { 9, 8.0 },   // 0.025 pixels per frame
            { 10, 10.0 }, // 0.02 pixels per frame
            { 11, 15.0 }, // 0.013 pixels per frame
            { 12, 20.0 }, // 0.01 pixels per frame
            { 13, 25.0 }, // 0.008 pixels per frame
        };

        // Helper method to safely get zoom factor
        public static double GetZoomFactor(int zoomLevel)
        {
            // If zoom level exists in dictionary, return it
            if (TimelineZooms.TryGetValue(zoomLevel, out double zoomFactor))
            {
                return zoomFactor;
            }

            // Otherwise return a default value based on range
            if (zoomLevel <= 0) return 1.0;
            if (zoomLevel > 13) return 30.0;

            // This should never happen with proper input validation
            return 1.0;
        }

        // Get the actual scale for a zoom level (pixels per frame)
        public static double GetPixelsPerFrame(int zoomLevel)
        {
            return TimelinePixelsInSeparator / GetZoomFactor(zoomLevel);
        }

        // Convert timeline frames to pixels at a given zoom level
        public static double FramesToPixels(int frames, int zoomLevel)
        {
            return frames * GetPixelsPerFrame(zoomLevel);
        }

        // Convert pixels to timeline frames at a given zoom level
        public static int PixelsToFrames(double pixels, int zoomLevel)
        {
            double pixelsPerFrame = GetPixelsPerFrame(zoomLevel);
            if (pixelsPerFrame <= 0) return 0; // Prevent division by zero

            return (int)(pixels / pixelsPerFrame);
        }
    }
}