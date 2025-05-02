using FFMpegCore;
using OpenCvSharp;
using PressPlay.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PressPlay.Models
{
    [Description("Project Clip Info")]
    [DisplayName("Project Clip Info")]
    [DebuggerDisplay("{FileName} {Length}")]
    public class ProjectClip : IProjectClip
    {
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
            // Debug information for troubleshooting
            System.Diagnostics.Debug.WriteLine($"Checking compatibility: Clip type {ItemType}, Track type {trackType}");

            bool isCompatible = trackType switch
            {
                TimelineTrackType.Video => ItemType == TrackItemType.Video || ItemType == TrackItemType.Image,
                TimelineTrackType.Audio => ItemType == TrackItemType.Audio,
                _ => false,
            };

            System.Diagnostics.Debug.WriteLine($"Compatibility result: {isCompatible}");
            return isCompatible;
        }

        public double GetWidth(int zoomLevel)
        {
            return Length.TotalFrames * Constants.TimelinePixelsInSeparator / Constants.TimelineZooms[zoomLevel];
        }

        // Add ClipId property for AudioTrackItem
        public string ClipId => Id;

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
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(FilePath))
                {
                    _fileHash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                }
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

            // **NEW**: make sure the target folder exists
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
                using (var capture = new VideoCapture(FilePath))
                {
                    var middleFrame = capture.FrameCount / 2;
                    capture.PosFrames = middleFrame;
                    using (var originalMat = capture.RetrieveMat())
                    {
                        var resizedMat = originalMat.Resize(new OpenCvSharp.Size(320, 240));
                        resizedMat.SaveImage(thumbnailPath);
                    }
                }
            }
            else if (FileFormats.SupportedAudioFormats.Contains(extension))
            {
                // placeholder logic—you could generate a waveform here
                Thumbnail = "audio_placeholder.png";
            }
            else if (FileFormats.SupportedImageFormats.Contains(extension))
            {
                // now safe to copy
                File.Copy(FilePath, thumbnailPath);
                Thumbnail = thumbnailPath;
            }

            // fallback
            if (string.IsNullOrEmpty(Thumbnail))
                Thumbnail = thumbnailPath;
        }

        public ProjectClipMetadata GetClipProperties()
        {
            var extension = Path.GetExtension(FilePath).ToLower();
            if (FileFormats.SupportedVideoFormats.Contains(extension))
            {
                var analysis = FFProbe.Analyse(FilePath);
                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Video,
                    ItemType = TrackItemType.Video,
                    Length = TimeCode.FromTimeSpan(analysis.Duration, analysis.PrimaryVideoStream.FrameRate),
                    FPS = analysis.PrimaryVideoStream.FrameRate,
                    Width = analysis.PrimaryVideoStream.Width,
                    Height = analysis.PrimaryVideoStream.Height
                };
            }
            if (FileFormats.SupportedAudioFormats.Contains(extension))
            {
                var analysis = FFProbe.Analyse(FilePath);
                return new ProjectClipMetadata
                {
                    TrackType = TimelineTrackType.Audio,
                    ItemType = TrackItemType.Audio,
                    Length = TimeCode.FromTimeSpan(analysis.Duration, FPS),
                    FPS = FPS,
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

        public void CacheFrames()
        {
            if (ItemType == TrackItemType.Video)
            {
                if (!File.Exists(TempFramesCacheFile))
                {
                    double max = 0;
                    using (var capture = new VideoCapture(FilePath))
                    {
                        var mat = new OpenCvSharp.Mat();
                        max = capture.FrameCount;
                        while (capture.Read(mat))
                        {
                            FramesCache.Add(new FrameCache(capture.PosFrames, mat.ToBytes(".jpg")));
                            var progress = ((double)capture.PosFrames / max) * 100d;
                            CacheProgress?.Invoke(this, progress);
                        }
                    }
                    using (var writer = File.Create(TempFramesCacheFile))
                    {
                        JsonSerializer.Serialize(writer, FramesCache);
                    }
                }
                else
                {
                    using (var reader = File.OpenRead(TempFramesCacheFile))
                    {
                        var cache = JsonSerializer.Deserialize<List<FrameCache>>(reader);
                        FramesCache.AddRange(cache);
                        CacheProgress?.Invoke(this, 100);
                    }
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