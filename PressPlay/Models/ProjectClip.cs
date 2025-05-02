using FFMpegCore;
using NAudio.Gui;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;  // for .ToBitmapSource()
using PressPlay.Helpers;
using System;
using System.Collections.Generic;
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
            if (_capture == null)
                _capture = new VideoCapture(FilePath);

            // compute and clamp frame index
            double fps = _capture.Fps > 0 ? _capture.Fps : FPS;
            int max = (int)_capture.FrameCount;
            int target = (int)Math.Round(position.TotalSeconds * fps);
            target = Math.Max(0, Math.Min(target, max - 1));
            _capture.PosFrames = target;

            // manually retrieve & possibly replace the Mat
            Mat mat = _capture.RetrieveMat();
            if (mat.Empty())
            {
                // ditch the empty one
                mat.Dispose();
                // create a black frame of the correct size
                mat = new Mat(_capture.FrameHeight,
                              _capture.FrameWidth,
                              MatType.CV_8UC3,
                              new Scalar(0, 0, 0));
            }

            // convert to WPF image
            var bmp = mat.ToBitmapSource();
            mat.Dispose();  // clean up
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
            var extension = Path.GetExtension(FilePath).ToLower();
            if (FileFormats.SupportedVideoFormats.Contains(extension))
            {
                try
                {
                    // Use multiple methods to determine duration reliably
                    using var capture = new VideoCapture(FilePath);
                    double captureFps = capture.Fps;
                    int frameCount = (int)capture.FrameCount;

                    // Try ffprobe analysis first
                    var analysis = FFProbe.Analyse(FilePath);
                    double ffprobeFps = analysis.PrimaryVideoStream?.FrameRate ?? 0;
                    TimeSpan ffprobeDuration = analysis.Duration;

                    // Select the most reliable values
                    double fps = ffprobeFps > 0 ? ffprobeFps : (captureFps > 0 ? captureFps : 25);
                    TimeSpan duration;

                    // Determine which duration is more reliable
                    if (ffprobeDuration.TotalSeconds > 0.5)
                    {
                        duration = ffprobeDuration;
                    }
                    else if (frameCount > 0 && captureFps > 0)
                    {
                        duration = TimeSpan.FromSeconds(frameCount / captureFps);
                    }
                    else
                    {
                        // Fallback to direct ffprobe call
                        duration = ProbeDuration(FilePath);

                        // If still invalid, use a reasonable default
                        if (duration.TotalSeconds < 0.5)
                        {
                            duration = TimeSpan.FromMinutes(1);
                        }
                    }

                    return new ProjectClipMetadata
                    {
                        TrackType = TimelineTrackType.Video,
                        ItemType = TrackItemType.Video,
                        Length = TimeCode.FromTimeSpan(duration, fps),
                        FPS = fps,
                        Width = analysis.PrimaryVideoStream?.Width ?? capture.FrameWidth,
                        Height = analysis.PrimaryVideoStream?.Height ?? capture.FrameHeight,
                        UnlimitedLength = false
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing video: {ex.Message}");

                    // Fallback if analysis completely fails
                    return new ProjectClipMetadata
                    {
                        TrackType = TimelineTrackType.Video,
                        ItemType = TrackItemType.Video,
                        Length = TimeCode.FromSeconds(60, 25), // 1 minute default
                        FPS = 25,
                        Width = 640,
                        Height = 480,
                        UnlimitedLength = false
                    };
                }
            }

            if (FileFormats.SupportedAudioFormats.Contains(extension))
            {
                // Use configured FPS or default
                var fps = FPS > 0 ? FPS : 25;
                var analysis = FFProbe.Analyse(FilePath);
                var duration = analysis.Duration;
                if (duration.TotalSeconds < 0.1)
                {
                    duration = ProbeDuration(FilePath);
                }
                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Audio,
                    ItemType = TrackItemType.Audio,
                    Length = TimeCode.FromTimeSpan(duration, fps),
                    FPS = fps,
                    Width = 0,
                    Height = 0
                };
            }

            if (FileFormats.SupportedImageFormats.Contains(extension))
            {
                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Video,
                    ItemType = TrackItemType.Image,
                    UnlimitedLength = true,
                    Length = TimeCode.FromSeconds(5, 25),
                    FPS = 25,
                    Width = 800,
                    Height = 600
                };
            }

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
