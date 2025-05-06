using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PressPlay.Helpers;
using PressPlay.Utilities;
using System.Drawing;
using PressPlay.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;

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
        private string _waveformImagePath;
        private bool _waveformGenerationInProgress;
        private float _volume = 1.0f;
        public Track.FadeColor FadeColor { get; set; } = Track.FadeColor.Black;
        public float Volume
        {
            get => _volume;
            set
            {
                if (_volume != value)
                {
                    _volume = Math.Clamp(value, 0.0f, 1.0f);
                    OnPropertyChanged();
                }
            }
        }
        // Waveform related properties
        public string WaveformImagePath
        {
            get => _waveformImagePath;
            private set
            {
                if (_waveformImagePath != value)
                {
                    _waveformImagePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasWaveform));
                }
            }
        }

        public bool HasWaveform => !string.IsNullOrEmpty(WaveformImagePath) && File.Exists(WaveformImagePath);

        // Standard ITrackItem properties implementation
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
                    // Clamp against source length
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
                    (End?.TotalFrames - Start?.TotalFrames) ?? 0,
                    Start?.FPS ?? 25);

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

                    // Generate waveform when file path changes
                    if (!string.IsNullOrEmpty(value) &&
                        File.Exists(value) &&
                        FileFormats.SupportedAudioFormats.Contains(
                            Path.GetExtension(value).ToLowerInvariant()))
                    {
                        GenerateWaveformAsync();
                    }
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
            // Better debugging to track compatibility issues
            Debug.WriteLine($"AudioTrackItem.IsCompatibleWith: TrackType={trackType}, FilePath={FilePath}");

            // Parse the trackType string to TimelineTrackType enum
            if (Enum.TryParse<TimelineTrackType>(trackType, out var parsedType))
            {
                bool isCompatible = parsedType == TimelineTrackType.Audio;
                Debug.WriteLine($"Compatibility result: {isCompatible}");
                return isCompatible;
            }

            // Direct string comparison as fallback
            bool stringCompatible = trackType.Equals("Audio", StringComparison.OrdinalIgnoreCase);
            Debug.WriteLine($"String compatibility fallback result: {stringCompatible}");
            return stringCompatible;
        }

        // Default constructor for serialization
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
        /// </summary>
        public AudioTrackItem(ProjectClip clip,
                              TimeCode position,
                              TimeCode start,
                              TimeCode length)
        {
            Debug.WriteLine($"Creating AudioTrackItem: Clip={clip?.FileName}, Position={position?.TotalFrames}, Start={start?.TotalFrames}, Length={length?.TotalFrames}");

            // Basic timing
            Position = position ?? new TimeCode(0, 25);
            Start = start ?? new TimeCode(0, 25);

            // Calculate end frame
            int endFrame = Start.TotalFrames;
            if (length != null)
                endFrame += length.TotalFrames;

            End = new TimeCode(endFrame, Start.FPS);
            OriginalEnd = End;

            // Default settings
            FadeInFrame = 0;
            FadeOutFrame = 0;
            IsSelected = false;
            _unlimitedSourceLength = false;

            // Clip info
            if (clip != null)
            {
                FileName = clip.FileName;
                FilePath = clip.FilePath;
                FullPath = clip.FilePath;
                SourceLength = clip.Length;
                ClipId = clip.Id;

                Debug.WriteLine($"Audio item source length: {SourceLength?.TotalFrames} frames");

                // Try to load thumbnail
                if (!string.IsNullOrEmpty(clip.Thumbnail) && File.Exists(clip.Thumbnail))
                {
                    try
                    {
                        Thumbnail = File.ReadAllBytes(clip.Thumbnail);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading thumbnail: {ex.Message}");
                    }
                }

                // Generate waveform
                if (!string.IsNullOrEmpty(FilePath) &&
                    File.Exists(FilePath) &&
                    FileFormats.SupportedAudioFormats.Contains(
                        Path.GetExtension(FilePath).ToLowerInvariant()))
                {
                    GenerateWaveformAsync();
                }
            }
        }

        /// <summary>
        /// Asynchronously generates a waveform image for the audio file
        /// </summary>
        private async void GenerateWaveformAsync()
        {
            // Skip if already generating or no file
            if (_waveformGenerationInProgress || string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
                return;

            _waveformGenerationInProgress = true;
            Debug.WriteLine($"Starting waveform generation for {FileName}");

            try
            {
                // Create directory for waveforms
                var waveformDir = Path.Combine(Path.GetTempPath(), "PressPlay", "Waveforms");
                Directory.CreateDirectory(waveformDir);

                // Create a unique hash for this audio file
                string hash = "";
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(FilePath))
                {
                    hash = BitConverter.ToString(md5.ComputeHash(stream))
                        .Replace("-", "")
                        .ToLowerInvariant();
                }

                // Path to store waveform image
                string waveformPath = Path.Combine(waveformDir, $"{hash}.png");

                // If waveform image already exists, use it immediately
                if (File.Exists(waveformPath))
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        WaveformImagePath = waveformPath;
                        Debug.WriteLine($"Using existing waveform: {waveformPath}");
                    });
                    return;
                }

                await Task.Run(() =>
                {
                    try
                    {
                        // Calculate width based on duration
                        int width = 600; // Default width
                        if (Duration != null && Duration.TotalFrames > 0)
                        {
                            // Scale width based on duration
                            double pixelsPerSecond = 100; // 100 pixels per second
                            width = (int)(Duration.TotalSeconds * pixelsPerSecond);
                            width = Math.Max(300, width); // Minimum width
                        }

                        // Set height to match track height minus padding
                        int height = (int)Constants.TrackHeight - 10;

                        Debug.WriteLine($"Generating waveform ({width}x{height}) for {FileName}");

                        // Generate the waveform
                        WaveFormGenerator.Generate(
                            width,
                            height,
                            Color.FromArgb(255, 0, 255, 0), // Light green with alpha
                            FilePath,
                            waveformPath
                        );

                        Debug.WriteLine($"Waveform generated: {waveformPath}");

                        // Update property on UI thread - this is critical!
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            WaveformImagePath = waveformPath;
                            Debug.WriteLine("Waveform path set on UI thread");
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating waveform: {ex.Message}");
                        Debug.WriteLine(ex.StackTrace);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting waveform generation: {ex.Message}");
            }
            finally
            {
                _waveformGenerationInProgress = false;
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

            // Ensure waveform is generated if needed
            if (!string.IsNullOrEmpty(FilePath) &&
                File.Exists(FilePath) &&
                FileFormats.SupportedAudioFormats.Contains(Path.GetExtension(FilePath).ToLowerInvariant()) &&
                string.IsNullOrEmpty(WaveformImagePath))
            {
                GenerateWaveformAsync();
            }
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