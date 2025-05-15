// PressPlay/Export/ExportSettings.cs
using System;
using System.IO;

namespace PressPlay.Export
{
    /// <summary>
    /// Settings for video export
    /// </summary>
    public class ExportSettings
    {
        /// <summary>
        /// Path to the output file
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// Width of the output video
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the output video
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Output video format
        /// </summary>
        public OutputFormat OutputFormat { get; set; } = OutputFormat.MP4;

        /// <summary>
        /// Video codec to use
        /// </summary>
        public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;

        /// <summary>
        /// Quality preset for the output video
        /// </summary>
        public VideoQuality VideoQuality { get; set; } = VideoQuality.High;

        /// <summary>
        /// Video bitrate in Kbps when using custom quality
        /// </summary>
        public int VideoBitrate { get; set; } = 10000;

        /// <summary>
        /// Audio bitrate in Kbps
        /// </summary>
        public int AudioBitrate { get; set; } = 192;

        /// <summary>
        /// Whether to include audio in the output
        /// </summary>
        public bool IncludeAudio { get; set; } = true;

        /// <summary>
        /// Custom FFmpeg arguments to pass to the encoder
        /// </summary>
        public string CustomFFmpegArgs { get; set; }

        /// <summary>
        /// Creates default export settings for the provided project dimensions
        /// </summary>
        public static ExportSettings CreateDefault(int width, int height)
        {
            return new ExportSettings
            {
                Width = width,
                Height = height,
                VideoQuality = VideoQuality.High,
                AudioBitrate = 192,
                IncludeAudio = true
            };
        }

        /// <summary>
        /// Generates a file extension based on the output format
        /// </summary>
        public string GetFileExtension()
        {
            return OutputFormat switch
            {
                OutputFormat.MP4 => ".mp4",
                OutputFormat.MKV => ".mkv",
                OutputFormat.AVI => ".avi",
                OutputFormat.MOV => ".mov",
                OutputFormat.WebM => ".webm",
                OutputFormat.GIF => ".gif",
                _ => ".mp4"
            };
        }

        /// <summary>
        /// Updates the output path based on the current format
        /// </summary>
        public void UpdateOutputPath()
        {
            if (string.IsNullOrEmpty(OutputPath))
                return;

            string dir = Path.GetDirectoryName(OutputPath);
            string filename = Path.GetFileNameWithoutExtension(OutputPath);

            // Set correct extension based on format
            OutputPath = Path.Combine(dir, filename + GetFileExtension());
        }
    }

    /// <summary>
    /// Output video formats
    /// </summary>
    public enum OutputFormat
    {
        MP4,
        MKV,
        AVI,
        MOV,
        WebM,
        GIF
    }

    /// <summary>
    /// Video codecs available for export
    /// </summary>
    public enum VideoCodec
    {
        H264,
        H265,
        VP9,
        ProRes
    }

    /// <summary>
    /// Quality presets for video export
    /// </summary>
    public enum VideoQuality
    {
        Low,
        Medium,
        High,
        Ultra,
        Custom
    }
}