using FFMpegCore;
using NAudio.Gui;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;  // for .ToBitmapSource()
using PressPlay.Effects;
using PressPlay.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace PressPlay.Models
{
    [Description("Project Clip Info")]
    [DisplayName("Project Clip Info")]
    [DebuggerDisplay("{FileName} {Length}")]
    public class ProjectClip : IProjectClip
    {
        // Static constructor to ensure ffprobe is configured
        static ProjectClip()
        {
            // Configure FFMpegCore to find ffprobe (adjust path or use environment variable as needed)
            var ffmpegDir = Environment.GetEnvironmentVariable("FFMPEG_CORE_DIR")
                            ?? @"D:\experimentz\PressPlay-master\PressPlay-master\PressPlay\bin\Debug\net8.0-windows\ffmpeg";
            GlobalFFOptions.Configure(options =>
            {
                options.BinaryFolder = ffmpegDir;
            });
        }

        private string _id = Guid.NewGuid().ToString().Replace("-", "");
        private TimelineTrackType _trackType;
        private TrackItemType _itemType;
        private string _fileHash;
        private string _filePath;
        private string _thumbnail;
        private bool _isSelected;
        private TimeCode _length;
        private int _width;
        private int _height;
        public bool HasAudio
        {
            get
            {
                // Always return true for video files (for testing/fixing)
                string ext = Path.GetExtension(FilePath)?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext) && FileFormats.SupportedVideoFormats.Contains(ext))
                {
                    Debug.WriteLine($"HasAudio getter: returning TRUE for {FileName}");
                    return true;
                }

                // For audio files, always true
                if (!string.IsNullOrEmpty(ext) && FileFormats.SupportedAudioFormats.Contains(ext))
                    return true;

                return false;
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<double> CacheProgress;

        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public TimelineTrackType TrackType { get => _trackType; set { _trackType = value; OnPropertyChanged(); } }
        public TrackItemType ItemType { get => _itemType; set { _itemType = value; OnPropertyChanged(); } }
        public string FilePath { get => _filePath; set { _filePath = value; OnFilePathChanged(); OnPropertyChanged(); } }
        public string FileName => Path.GetFileName(FilePath);
        public int Width { get => _width; set { _width = value; OnPropertyChanged(); } }
        public int Height { get => _height; set { _height = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public TimeCode Length { get => _length; set { _length = value; OnPropertyChanged(); } }
        public bool UnlimitedLength { get; private set; }
        public ObservableCollection<IEffect> Effects { get; } = new ObservableCollection<IEffect>();
        public double FPS { get; private set; }
        public string Thumbnail { get => _thumbnail; set { _thumbnail = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string TempFramesCacheFile => Path.Combine(Path.GetTempPath(), "PressPlay", $"clip_{_id}.cache");
        [JsonIgnore]
        public List<FrameCache> FramesCache { get; } = new List<FrameCache>();

        public ProjectClip() { }
        public ProjectClip(string filePath) { FilePath = filePath; }
        public ProjectClip(string filePath, double fps) { FilePath = filePath; FPS = fps; }

        public bool IsCompatibleWith(TimelineTrackType trackType)
        {
            Debug.WriteLine($"Checking compatibility: Clip type {ItemType}, Track type {trackType}");
            bool isCompatible = trackType switch
            {
                TimelineTrackType.Video => ItemType == TrackItemType.Video || ItemType == TrackItemType.Image,
                TimelineTrackType.Audio => ItemType == TrackItemType.Audio,
                _ => false,
            };
            Debug.WriteLine($"Compatibility result: {isCompatible}");
            return isCompatible;
        }
        public double GetWidth(int zoomLevel)
            => Length.TotalFrames * Constants.TimelinePixelsInSeparator / Constants.TimelineZooms[zoomLevel];

        public string ClipId => Id;
        private VideoCapture _capture;
        public BitmapSource GetFrameAt(TimeSpan position)
        {
            // 1) Handle still images
            var ext = Path.GetExtension(FilePath).ToLower();
            if (FileFormats.SupportedImageFormats.Contains(ext))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(FilePath);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                return bi;
            }

            // 2) Lazy‐init video capture
            if (_capture == null)
                _capture = new VideoCapture(FilePath);

            // 3) Compute frame index
            double fps = _capture.Fps > 0 ? _capture.Fps : FPS;
            int maxFrames = (int)_capture.FrameCount;
            int target = (int)Math.Round(position.TotalSeconds * fps);
            target = Math.Clamp(target, 0, maxFrames - 1);
            _capture.PosFrames = target;

            // 4) Retrieve the raw frame
            Mat mat = _capture.RetrieveMat();
            if (mat.Empty())
            {
                mat.Dispose();
                mat = new Mat(
                    _capture.FrameHeight,
                    _capture.FrameWidth,
                    MatType.CV_8UC3,
                    new Scalar(0, 0, 0)
                );
            }

            // 5) Apply all registered effects, in order
            foreach (var fx in Effects)
            {
                // in‐place: each effect writes into the same Mat
                fx.ProcessFrame(mat, mat);
            }

            // 6) Convert to WPF BitmapSource and clean up
            var bmp = mat.ToBitmapSource();
            mat.Dispose();
            return bmp;
        }



        private void GetInfo()
        {
            var properties = GetClipProperties();
            UnlimitedLength = properties.UnlimitedLength;
            TrackType = properties.TrackType;
            ItemType = properties.ItemType;
            Length = properties.Length;
            FPS = properties.FPS;
            Width = properties.Width;
            Height = properties.Height;
        }

        private void GetThumbnail() => CreateThumbnail();

        public string GetFileHash()
        {
            if (string.IsNullOrWhiteSpace(_fileHash))
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(FilePath);
                _fileHash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
            }
            return _fileHash;
        }

        private void OnFilePathChanged()
        {
            GetInfo();
            GetThumbnail();
        }

        public void CreateThumbnail()
        {
            var extension = Path.GetExtension(FilePath).ToLower();
            var thumbnailPath = Path.Combine(Path.GetTempPath(), "PressPlay", $"{GetFileHash()}_thumbnail.png");

            var folder = Path.GetDirectoryName(thumbnailPath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            if (File.Exists(thumbnailPath))
            {
                Thumbnail = thumbnailPath;
                return;
            }

            if (FileFormats.SupportedVideoFormats.Contains(extension))
            {
                using var capture = new VideoCapture(FilePath);
                var middleFrame = capture.FrameCount / 2;
                capture.PosFrames = middleFrame;
                using var originalMat = capture.RetrieveMat();
                var resizedMat = originalMat.Resize(new OpenCvSharp.Size(320, 240));
                resizedMat.SaveImage(thumbnailPath);
            }
            else if (FileFormats.SupportedAudioFormats.Contains(extension))
            {
                Thumbnail = "audio_placeholder.png";
            }
            else if (FileFormats.SupportedImageFormats.Contains(extension))
            {
                File.Copy(FilePath, thumbnailPath);
                Thumbnail = thumbnailPath;
            }

            if (string.IsNullOrEmpty(Thumbnail))
                Thumbnail = thumbnailPath;
        }

        /// <summary>
        /// Uses FFProbe to get media properties, with a fallback direct ffprobe call if duration is invalid.
        /// </summary>
        public ProjectClipMetadata GetClipProperties()
        {
            var extension = Path.GetExtension(FilePath).ToLowerInvariant();

            // --- VIDEO ---
            if (FileFormats.SupportedVideoFormats.Contains(extension))
            {
                Debug.WriteLine($"Analyzing video file: {FilePath}");

                // FFProbe analysis to detect streams
                bool hasAudio = false;
                double fps = 25;
                long frameCount = 0;
                int width = 640;
                int height = 480;

                try
                {
                    // Be explicit about FFmpeg path
                    string ffmpegDir = Environment.GetEnvironmentVariable("FFMPEG_CORE_DIR")
                        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");

                    // Configure FFMpegCore with explicit path
                    FFMpegCore.GlobalFFOptions.Configure(options => {
                        options.BinaryFolder = ffmpegDir;
                        Debug.WriteLine($"Configured FFMpegCore with path: {ffmpegDir}");
                    });

                    var mediaInfo = FFProbe.Analyse(FilePath);

                    // Check for audio streams
                    hasAudio = mediaInfo.AudioStreams?.Any() == true;
                    Debug.WriteLine($"FFProbe detected audio streams: {hasAudio}");

                    if (mediaInfo.PrimaryVideoStream != null)
                    {
                        fps = mediaInfo.PrimaryVideoStream.FrameRate > 0
                            ? mediaInfo.PrimaryVideoStream.FrameRate
                            : 25;
                        width = mediaInfo.PrimaryVideoStream.Width > 0
                            ? mediaInfo.PrimaryVideoStream.Width
                            : 640;
                        height = mediaInfo.PrimaryVideoStream.Height > 0
                            ? mediaInfo.PrimaryVideoStream.Height
                            : 480;

                        // Calculate frameCount 
                        double durationSeconds = mediaInfo.Duration.TotalSeconds;
                        frameCount = (long)Math.Ceiling(durationSeconds * fps);

                        Debug.WriteLine($"Video properties: FPS={fps}, Duration={durationSeconds}s, Frames={frameCount}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FFProbe analysis failed: {ex.Message}");
                    // Fallback to OpenCV for video properties
                }

                // Fallback to OpenCV if needed
                if (frameCount <= 0 || width <= 0 || height <= 0)
                {
                    try
                    {
                        using var capture = new VideoCapture(FilePath);
                        if (!capture.IsOpened())
                            throw new IOException($"Cannot open video file: {FilePath}");

                        // Read video properties
                        fps = capture.Fps > 0 ? capture.Fps : fps;
                        frameCount = frameCount <= 0 ? (long)capture.FrameCount : frameCount;
                        width = width <= 0 ? capture.FrameWidth : width;
                        height = height <= 0 ? capture.FrameHeight : height;

                        Debug.WriteLine($"OpenCV properties: FPS={fps}, Frames={frameCount}, Size={width}x{height}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"OpenCV analysis failed: {ex.Message}");
                    }
                }

                // Force HasAudio to true for testing if FFProbe didn't detect it
                // Remove this in production code
                if (!hasAudio)
                {
                    Debug.WriteLine("No audio detected - forcing HasAudio=true for testing purposes");
                    hasAudio = true;
                }

                // Compute duration
                TimeSpan duration = TimeSpan.FromSeconds(frameCount / fps);

                // Create metadata
                var metadata = new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Video,
                    ItemType = TrackItemType.Video,
                    HasAudio = hasAudio,
                    FPS = fps,
                    Length = TimeCode.FromTimeSpan(duration, fps),
                    Width = width,
                    Height = height,
                    UnlimitedLength = false
                };

                Debug.WriteLine($"Final metadata: HasAudio={metadata.HasAudio}, Length={metadata.Length.TotalSeconds}s");
                return metadata;
            }

            // --- AUDIO ---
            if (FileFormats.SupportedAudioFormats.Contains(extension))
            {
                // Try FFProbe first
                TimeSpan duration;
                double fps = FPS > 0 ? FPS : 25;

                try
                {
                    var info = FFProbe.Analyse(FilePath);
                    duration = info.Duration;
                    if (duration.TotalSeconds < 0.1)
                        throw new InvalidOperationException("FFProbe duration too small");
                }
                catch
                {
                    // Fallback to your ProbeDuration helper
                    duration = ProbeDuration(FilePath);
                }

                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Audio,
                    ItemType = TrackItemType.Audio,
                    FPS = fps,
                    Length = TimeCode.FromTimeSpan(duration, fps),
                    Width = 0,
                    Height = 0,
                    UnlimitedLength = false
                };
            }

            // --- IMAGE ---
            if (FileFormats.SupportedImageFormats.Contains(extension))
            {
                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Video,
                    ItemType = TrackItemType.Image,
                    UnlimitedLength = true,
                    // 5 seconds at 25fps by default
                    FPS = 25,
                    Length = TimeCode.FromSeconds(5, 25),
                    Width = 800,
                    Height = 600
                };
            }

            // --- UNKNOWN FORMAT ---
            return new ProjectClipMetadata();
        }


        /// <summary>
        /// Fallback direct ffprobe call to get duration in case FFMpegCore Analyse fails.
        /// </summary>
        private static TimeSpan ProbeDuration(string path)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration " +
                                $"-of default=nw=1:nk=1 \"{path}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(startInfo);
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out var secs))
                {
                    return TimeSpan.FromSeconds(secs);
                }
            }
            catch { /* ignore and fallback */ }
            return TimeSpan.Zero;
        }

        public void CacheFrames()
        {
            if (ItemType == TrackItemType.Video)
            {
                if (!File.Exists(TempFramesCacheFile))
                {
                    double max = 0;
                    using var capture = new VideoCapture(FilePath);
                    var mat = new Mat();
                    max = capture.FrameCount;
                    while (capture.Read(mat))
                    {
                        FramesCache.Add(new FrameCache(capture.PosFrames, mat.ToBytes(".jpg")));
                        var progress = ((double)capture.PosFrames / max) * 100d;
                        CacheProgress?.Invoke(this, progress);
                    }
                    using var writer = File.Create(TempFramesCacheFile);
                    JsonSerializer.Serialize(writer, FramesCache);
                }
                else
                {
                    using var reader = File.OpenRead(TempFramesCacheFile);
                    var cache = JsonSerializer.Deserialize<List<FrameCache>>(reader);
                    FramesCache.AddRange(cache);
                    CacheProgress?.Invoke(this, 100);
                }
            }
            else if (ItemType == TrackItemType.Image)
            {
                FramesCache.Add(new FrameCache(0, File.ReadAllBytes(FilePath)));
            }
        }

        public string GenerateNewId()
        {
            Id = Guid.NewGuid().ToString().Replace("-", "");
            return Id;
        }

        public void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
