using System.ComponentModel;
using System.Windows.Media.Imaging;
using static PressPlay.MainWindowViewModel;
using PressPlay.Helpers;

namespace PressPlay.Models
{
    public class Clip : ITrackItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private byte[] _thumbnailBytes;
        private BitmapImage _thumbnailImage;

        public double StartTime { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string FullPath { get; set; }

        // This property is now private since we need to expose a byte[] for the interface
        private BitmapImage ThumbnailImage
        {
            get => _thumbnailImage;
            set
            {
                _thumbnailImage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailImage)));
            }
        }

        // ITrackItem interface requires this to be byte[]
        public byte[] Thumbnail
        {
            get => _thumbnailBytes;
            set
            {
                _thumbnailBytes = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));

                // Convert bytes to BitmapImage when set for internal use
                if (value != null)
                {
                    try
                    {
                        BitmapImage image = new BitmapImage();
                        using (System.IO.MemoryStream ms = new System.IO.MemoryStream(value))
                        {
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.StreamSource = ms;
                            image.EndInit();
                            image.Freeze(); // Freeze for UI thread safety
                        }
                        ThumbnailImage = image;
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error converting thumbnail bytes to image: {ex.Message}");
                    }
                }
            }
        }

        // Timeline properties:
        public TimeCode Position { get; set; }
        public TimeCode Start { get; set; }
        public TimeCode End { get; set; }

        // For convenience, we define Duration as the difference between End and Start.
        public TimeCode Duration => new TimeCode(End?.TotalFrames - Start?.TotalFrames ?? 0, Start?.FPS ?? 25);

        public int FadeInFrame { get; set; }
        public int FadeOutFrame { get; set; }
        public bool IsSelected { get; set; }
        public bool IsChangingFadeIn { get; private set; }
        public bool IsChangingFadeOut { get; private set; }
        public bool UnlimitedSourceLength { get; private set; }
        private TimeCode _sourceLength;
        public TimeCode SourceLength
        {
            get => _sourceLength;
            set
            {
                if (_sourceLength != value)
                {
                    _sourceLength = value;
                    OnPropertyChanged(nameof(SourceLength));
                }
            }
        }
        public bool IsCompatibleWith(string trackType)
        {
            // Simple example: return true if trackType is "Video" and this is a video clip.
            return trackType == "Video";
        }

        public void Initialize()
        {
            // No special setup needed for Clip, but this satisfies the interface.
        }

        // Added missing methods required by ITrackItem interface
        public double GetScaledPosition(int zoomLevel)
        {
            double zoomFactor = Constants.TimelineZooms.ContainsKey(zoomLevel) ?
                Constants.TimelineZooms[zoomLevel] : 1.0;

            return Position.TotalFrames * Constants.TimelinePixelsInSeparator / zoomFactor;
        }

        public double GetScaledWidth(int zoomLevel)
        {
            double zoomFactor = Constants.TimelineZooms.ContainsKey(zoomLevel) ?
                Constants.TimelineZooms[zoomLevel] : 1.0;

            return Duration.TotalFrames * Constants.TimelinePixelsInSeparator / zoomFactor;
        }

        // Helper method for null-safe operations
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}