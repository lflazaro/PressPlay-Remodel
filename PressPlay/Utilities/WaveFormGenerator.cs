using System;
using System.Drawing;
using System.IO;
using NAudio.Wave;
using NAudio.WaveFormRenderer;

namespace PressPlay.Utilities
{
    public static class WaveFormGenerator
    {
        /// <summary>
        /// Generate a waveform PNG from any file MediaFoundation/NAudio can read (MP3, WAV, etc.).
        /// </summary>
        public static void Generate(int width, int height, Color background, string audioFilePath, string outputFilePath)
        {
            // AudioFileReader handles MP3, WAV, WMA, AAC, etc. via MediaFoundation under the covers
            using (var reader = new AudioFileReader(audioFilePath))
            {
                Generate(width, height, background, (WaveStream)reader, outputFilePath);
            }
        }

        /// <summary>
        /// Core renderer: from any WaveStream, draw and save a PNG.
        /// </summary>
        public static void Generate(int width, int height, Color background, WaveStream waveStream, string outputFilePath)
        {
            var settings = CreateSettings(width, height, background);
            var renderer = new WaveFormRenderer();

            // Render returns a System.Drawing.Bitmap
            using (var bmp = renderer.Render(waveStream, settings))
            {
                // Ensure target directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? ".");
                bmp.Save(outputFilePath);
            }
        }

        /// <summary>
        /// Shared waveform‐style settings.
        /// </summary>
        private static StandardWaveFormRendererSettings CreateSettings(int width, int height, Color background)
        {
            return new StandardWaveFormRendererSettings
            {
                Width = width,
                TopHeight = height / 2,
                BottomHeight = height / 2,
                BackgroundColor = background,

                // Customize these pens however you like
                TopPeakPen = new Pen(Color.LightGreen, 1),
                BottomPeakPen = new Pen(Color.LightGreen, 1),
            };
        }
    }
}
