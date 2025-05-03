using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PressPlay.Helpers;
using PressPlay.Utilities;            // for WaveFormGenerator
using System.Drawing;               // for Color
using PressPlay.Models;
using System.Diagnostics;

namespace PressPlay.Models
{
    public class AudioTrackItem : ITrackItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // Backing fields
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
        private TimeCode _originalEnd;

        // Waveform image
        public string WaveformImagePath { get; private set; }
        public bool HasWaveform => !string.IsNullOrEmpty(WaveformImagePath)
                                            && File.Exists(WaveformImagePath);

        public TimeCode OriginalEnd
        {
            get => _originalEnd;
            set
            {
                if (_originalEnd != value)
                {
                    _originalEnd = value;
                    OnPropertyChanged();
                }
            }
        }

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
                    // clamp against source length
                    if (!UnlimitedSourceLength && SourceLength != null)
                    {
                        int maxFrames = (int)SourceLength.TotalFrames;
                        if (value.TotalFrames > maxFrames)
                            value = new TimeCode(maxFrames, value.FPS);
                    }

                    _end = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        public TimeCode Duration
            => new TimeCode(
                    (End.TotalFrames - Start.TotalFrames),
                    Start.FPS);
        private string _clipId;
        public string ClipId
        {
            get => _clipId;
            set
            {
                if (_clipId != value)
                {
                    _clipId = value;
                    OnPropertyChanged();
                }
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

        public string FullPath { get; set; }

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
            return Enum.TryParse<TimelineTrackType>(trackType, out var t)
                   && t == TimelineTrackType.Audio;
        }

        // Default ctor for serialization etc.
        public AudioTrackItem()
        {
            Position = new TimeCode(0, 25);
            Start = new TimeCode(0, 25);
            End = new TimeCode(10, 25);
            OriginalEnd = End;
            FadeInFrame = 0;
            FadeOutFrame = 0;
            IsSelected = false;
            _unlimitedSourceLength = false;
        }

        /// <summary>
        /// Main constructor used when dropping an audio clip onto the timeline.
        /// Generates a waveform PNG under %TEMP%\PressPlay\waveforms\.
        /// </summary>
        public AudioTrackItem(ProjectClip clip,
                              TimeCode position,
                              TimeCode start,
                              TimeCode length)
        {
            // 1) Basic timing
            Position = position ?? new TimeCode(0, 25);
            Start = start ?? new TimeCode(0, 25);
            int endF = (Start.TotalFrames + (length?.TotalFrames ?? 0));
            End = new TimeCode(endF, Start.FPS);
            OriginalEnd = End;

            FadeInFrame = 0;
            FadeOutFrame = 0;
            IsSelected = false;
            _unlimitedSourceLength = false;

            // 2) Clip info
            if (clip != null)
            {
                FileName = clip.FileName;
                FilePath = clip.FilePath;
                FullPath = clip.FilePath;
                SourceLength = clip.Length;
            }

            // 3) Try to load thumbnail for icon
            if (clip != null && !string.IsNullOrEmpty(clip.Thumbnail))
            {
                try
                {
                    Thumbnail = File.ReadAllBytes(clip.Thumbnail);
                }
                catch { /* ignore */ }
            }

            // 4) Generate waveform image if this clip has audio
            try
            {
                // Only for non-zero-length audio
                if (SourceLength.TotalFrames > 0)
                {
                    // Prepare temp folder
                    var folder = Path.Combine(
                        Path.GetTempPath(),
                        "PressPlay", "waveforms");
                    Directory.CreateDirectory(folder);

                    // Unique PNG name per clip
                    var hash = clip.GetFileHash();
                    WaveformImagePath = Path.Combine(
                        folder,
                        $"{hash}.png");

                    if (!File.Exists(WaveformImagePath))
                    {
                        // Compute pixel width at zoom 1
                        double pxPerFrame = Constants.TimelinePixelsInSeparator
                                            / Constants.TimelineZooms[1];
                        int width = (int)Math.Ceiling(Duration.TotalFrames * pxPerFrame);
                        int height = (int)Constants.TrackHeight; // you can adjust this

                        WaveFormGenerator.Generate(
                            width,
                            height,
                            Color.Black,     // background
                            clip.FilePath,
                            WaveformImagePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Waveform generation failed: {ex.Message}");
                WaveformImagePath = null;
            }
        }

        public void Initialize()
        {
            if (Position == null) Position = new TimeCode(0, 25);
            if (Start == null) Start = new TimeCode(0, 25);
            if (End == null) End = new TimeCode(10, 25);
            if (OriginalEnd == null) OriginalEnd = End;

            Debug.WriteLine($"[AudioTrackItem] Initialized: " +
                $"{FileName} | Pos={Position.TotalFrames} " +
                $"Start={Start.TotalFrames} End={End.TotalFrames}");
        }
        public double GetScaledPosition(int zoomLevel) =>
            Position.TotalFrames
            * Constants.TimelinePixelsInSeparator
            / Constants.TimelineZooms[zoomLevel];

        public double GetScaledWidth(int zoomLevel) =>
            Duration.TotalFrames
            * Constants.TimelinePixelsInSeparator
            / Constants.TimelineZooms[zoomLevel];

        protected void OnPropertyChanged([CallerMemberName] string propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
