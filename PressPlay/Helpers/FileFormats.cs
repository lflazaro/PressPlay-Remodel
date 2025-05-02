using System.Collections.Generic;

namespace PressPlay.Helpers
{
    public static class FileFormats
    {
        public static readonly HashSet<string> SupportedVideoFormats = new HashSet<string> { ".mp4", ".avi", ".mov", ".mkv" };
        public static readonly HashSet<string> SupportedAudioFormats = new HashSet<string> { ".mp3", ".wav", ".aac" };
        public static readonly HashSet<string> SupportedImageFormats = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };
    }
}
