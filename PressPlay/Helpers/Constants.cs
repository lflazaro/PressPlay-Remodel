using System.Collections.Generic;

namespace PressPlay.Helpers
{
    public static class Constants
    {
        // Pixels per frame at zoom level 1
        public static double TimelinePixelsInSeparator = 0.2;

        // Zoom levels with multipliers
        public static Dictionary<int, double> TimelineZooms = new Dictionary<int, double>
        {
            { 1, 1.0 },   // 0.2 pixels per frame
            { 2, 1.5 },   // 0.3 pixels per frame
            { 3, 2.0 },   // 0.4 pixels per frame
            { 4, 3.0 },   // 0.6 pixels per frame
            { 5, 4.0 },   // 0.8 pixels per frame
            { 6, 5.0 },   // 1.0 pixels per frame
            { 7, 6.0 },   // 1.2 pixels per frame
            { 8, 8.0 },   // 1.6 pixels per frame
            { 9, 10.0 },  // 2.0 pixels per frame
            { 10, 15.0 }, // 3.0 pixels per frame
            { 11, 20.0 }, // 4.0 pixels per frame
            { 12, 25.0 }, // 5.0 pixels per frame
            { 13, 30.0 }, // 6.0 pixels per frame
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
            return TimelinePixelsInSeparator * GetZoomFactor(zoomLevel);
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