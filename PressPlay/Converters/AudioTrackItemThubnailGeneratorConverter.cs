using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
            if (project == null || trackItem == null)
                return null;

            // Find the clip metadata
            var clip = project.Clips
                              .OfType<ProjectClip>()
                              .FirstOrDefault(c => c.Id == trackItem.ClipId);
            if (clip == null)
                return null;

            // Compute how wide the waveform should be
            var clipWidth = clip.GetWidth(project.TimelineZoom);
            if (clipWidth <= 0)
                return null;

            // Ensure our cache folder exists
            Directory.CreateDirectory(CacheRoot);

            const int ThumbHeight = 50;
            string cacheFileName = $"{clip.GetFileHash()}_{(int)project.TimelineZoom}_{(int)clipWidth}_{ThumbHeight}.png";
            string thumbPath = Path.Combine(CacheRoot, cacheFileName);

            // Generate the PNG if it isn't already cached
            if (!File.Exists(thumbPath))
            {
                try
                {
                    // <- Correct positional overload:
                    WaveFormGenerator.Generate(
                        (int)clipWidth,
                        ThumbHeight,
                        System.Drawing.Color.Transparent,
                        clip.FilePath,
                        thumbPath
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WaveFormGenerator] failed for {clip.FilePath}: {ex}");
                    return null;
                }
            }

            // Load it into a freezable BitmapImage so WPF can display it without locking the file
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(thumbPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WaveformLoad] failed for {thumbPath}: {ex}");
                return null;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
