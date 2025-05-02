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
            if (_capture == null)
                _capture = new VideoCapture(FilePath);

            // determine FPS and max frame count
            double fps = _capture.Fps > 0 ? _capture.Fps : FPS;
            int maxFrames = (int)_capture.FrameCount;

            // translate TimeSpan → zero-based frame index
            int target = (int)Math.Round(position.TotalSeconds * fps);
            target = Math.Max(0, Math.Min(target, maxFrames - 1));
            _capture.PosFrames = target;

            // pull the frame
            Mat mat = _capture.RetrieveMat();
            if (mat.Empty())
            {
                mat.Dispose();
                // produce a black frame as a fallback
                mat = new Mat(_capture.FrameHeight,
                              _capture.FrameWidth,
                              MatType.CV_8UC3,
                              new Scalar(0, 0, 0));
            }

            // convert & clean up
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
                // 1) Open with OpenCvSharp
                using var cap = new VideoCapture(FilePath);
                if (!cap.IsOpened())
                    throw new IOException($"Cannot open video file: {FilePath}");

                // 2) Read FPS and frame count
                double fps = cap.Fps > 0 ? cap.Fps : 25;
                double rawFrameCount = (double)cap.FrameCount;
                long frameCount = (long)Math.Round(rawFrameCount);

                // 3) Compute true duration
                var duration = TimeSpan.FromSeconds(frameCount / fps);

                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Video,
                    ItemType = TrackItemType.Video,
                    FPS = fps,
                    Length = TimeCode.FromTimeSpan(duration, fps),
                    Width = cap.FrameWidth,
                    Height = cap.FrameHeight,
                    UnlimitedLength = false
                };
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
