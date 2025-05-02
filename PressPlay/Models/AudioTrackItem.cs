using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PressPlay.Helpers;

namespace PressPlay.Models
{
    public class AudioTrackItem : ITrackItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private double _startTime;
        private TimeCode _position;
        private TimeCode _start;
        private TimeCode _end;
        private int _fadeInFrame;
        private int _fadeOutFrame;
        private bool _isSelected;
        private bool _isChangingFadeIn;
        private bool _isChangingFadeOut;
        private bool _unlimitedSourceLength;
        private string _fileName;
        private string _filePath;
        private byte[] _thumbnail;
        private TimeCode _sourceLength;
        public TimeCode SourceLength
        {
            get => _sourceLength;
            set
            {
                if (_sourceLength != value)
                {
                    _sourceLength = value;
                    OnPropertyChanged();
                }
            }
        }
        public double StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime != value)
                {
                    _startTime = value;
                    OnPropertyChanged();
                }
            }
        }
        public TimeCode Position
        {
            get => _position;
            set
            {
                if (_position?.TotalFrames != value?.TotalFrames)
                {
                    _position = value;
                    OnPropertyChanged();
                    System.Diagnostics.Debug.WriteLine($"AudioTrackItem Position updated to frame {value?.TotalFrames}");
                }
            }
        }

        public TimeCode Start
        {
            get => _start;
            set
            {
                if (_start?.TotalFrames != value?.TotalFrames)
                {
                    _start = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeCode End
        {
            get => _end;
            set
            {
                if (_end?.TotalFrames != value?.TotalFrames)
                {
                    // If not unlimited source length, validate against source length
                    if (!UnlimitedSourceLength)
                    {
                        // Determine max duration from file type
                        int maxDuration = int.MaxValue;
                        if (Path.GetExtension(FilePath)?.ToLowerInvariant() != null &&
                            FileFormats.SupportedAudioFormats.Contains(Path.GetExtension(FilePath).ToLowerInvariant()))
                        {
                            // For audio, source length is fixed
                            maxDuration = End?.TotalFrames ?? 0; // Use original end value as max
                        }

                        if (value.TotalFrames > maxDuration)
                        {
                            value = new TimeCode(maxDuration, value.FPS);
                        }
                    }

                    _end = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        public TimeCode Duration
        {
            get
            {
                int startFrames = Start?.TotalFrames ?? 0;
                int endFrames = End?.TotalFrames ?? 0;
                double fps = Start?.FPS ?? 25;
                return new TimeCode(endFrames - startFrames, fps);
            }
        }

        public int FadeInFrame
        {
            get => _fadeInFrame;
            set
            {
                if (_fadeInFrame != value)
                {
                    _fadeInFrame = value;
                    OnPropertyChanged();
                }
            }
        }

        public int FadeOutFrame
        {
            get => _fadeOutFrame;
            set
            {
                if (_fadeOutFrame != value)
                {
                    _fadeOutFrame = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsChangingFadeIn => _isChangingFadeIn;
        public bool IsChangingFadeOut => _isChangingFadeOut;
        public bool UnlimitedSourceLength => _unlimitedSourceLength;

        // Additional properties to help with visualization
        public string FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _fullPath;
        public string FullPath
        {
            get => string.IsNullOrEmpty(_fullPath) ? FilePath : _fullPath;
            set => _fullPath = value;
        }

        public byte[] Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public bool IsCompatibleWith(string trackType)
        {
            if (Enum.TryParse<TimelineTrackType>(trackType, out var parsedType))
            {
                return parsedType == TimelineTrackType.Audio;
            }
            return false;
        }

        public AudioTrackItem()
        {
            Position = new TimeCode(0, 25);
            Start = new TimeCode(0, 25);
            End = new TimeCode(10, 25); // Default to a small duration
            FadeInFrame = 0;
            FadeOutFrame = 0;
            IsSelected = false;
            _unlimitedSourceLength = false;
        }

        public AudioTrackItem(ProjectClip clip, TimeCode position, TimeCode start, TimeCode length)
        {
            Position = position ?? new TimeCode(0, 25);
            Start = start ?? new TimeCode(0, 25);

            // Compute End by adding length (in frames) to Start.TotalFrames
            int endFrames = (start?.TotalFrames ?? 0) + (length?.TotalFrames ?? 10);
            End = new TimeCode(endFrames, start?.FPS ?? 25);

            // Set default fade values and other properties.
            FadeInFrame = 0;
            FadeOutFrame = 0;
            IsSelected = false;
            _unlimitedSourceLength = false; // Audio files always have fixed length

            // Save clip info for visualization
            if (clip != null)
            {
                FileName = clip.FileName;
                FilePath = clip.FilePath;
                FullPath = clip.FilePath;

                // Try to set thumbnail if available
                if (!string.IsNullOrEmpty(clip.Thumbnail) && File.Exists(clip.Thumbnail))
                {
                    try
                    {
                        Thumbnail = File.ReadAllBytes(clip.Thumbnail);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading thumbnail: {ex.Message}");
                    }
                }
            }

            Initialize();
        }

        public void Initialize()
        {
            // Make sure we have valid objects
            if (Position == null) Position = new TimeCode(0, 25);
            if (Start == null) Start = new TimeCode(0, 25);
            if (End == null) End = new TimeCode(10, 25);

            System.Diagnostics.Debug.WriteLine($"Audio track item initialized: {FileName}, Position: {Position?.TotalFrames}, " +
                $"Start: {Start?.TotalFrames}, End: {End?.TotalFrames}");
        }

        // Methods needed for interface implementation
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

        protected void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}