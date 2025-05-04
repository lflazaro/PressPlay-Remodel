using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using PressPlay.Models;
using PressPlay.Timeline;
using PressPlay.Utilities;

namespace PressPlay.Converters
{
    public class AudioTrackItemThumbnailGeneratorConverter : MarkupExtension, IMultiValueConverter
    {
        // Where we cache generated waveforms
        static readonly string CacheRoot = Path.Combine(Path.GetTempPath(), "PressPlay", "Waveforms");
        private static AudioTrackItemThumbnailGeneratorConverter _instance;

        public override object ProvideValue(IServiceProvider serviceProvider)
            => _instance ??= new AudioTrackItemThumbnailGeneratorConverter();

        /// <summary>
        /// values[0] = Project
        /// values[1] = AudioTrackItem
        /// values[2] = (optional) TrackItemControl
        /// </summary>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var project = values.OfType<Project>().FirstOrDefault();
            var trackItem = values.OfType<AudioTrackItem>().FirstOrDefault();
            var control = values.OfType<FrameworkElement>().FirstOrDefault();  // the TrackItemControl

            if (project == null || trackItem == null || control == null)
                return null;

            // Find the clip
            var clip = project.Clips
                              .OfType<ProjectClip>()
                              .FirstOrDefault(c => c.Id == trackItem.ClipId);
            if (clip == null)
                return null;

            // Compute width from zoom
            var clipWidth = clip.GetWidth(project.TimelineZoom);
            if (clipWidth <= 0)
                return null;

            // **Use the control's ActualHeight** (once measured) for the waveform height
            int height = (int)Math.Round(control.ActualHeight);
            // Fallback if ActualHeight hasn't been set yet
            if (height < 20) height = 50;

            // Cache path logic unchanged...
            var cacheFolder = Path.Combine(Path.GetTempPath(), "PressPlay", "Waveforms");
            Directory.CreateDirectory(cacheFolder);
            string fileName = $"{clip.GetFileHash()}_{project.TimelineZoom}_{clipWidth}_{height}.png";
            var wavePath = Path.Combine(cacheFolder, fileName);

            if (!File.Exists(wavePath))
            {
                try
                {
                    WaveFormGenerator.Generate(
                        (int)clipWidth,
                        height,
                        Color.Transparent,
                        clip.FilePath,
                        wavePath
                    );
                }
                catch { return null; }
            }

            // Load BitmapImage
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(wavePath);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
