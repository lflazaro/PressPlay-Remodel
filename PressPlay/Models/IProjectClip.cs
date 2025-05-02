using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace PressPlay.Models
{
    /// <summary>
    /// Represents a single clip in a project.
    /// </summary>
    public interface IProjectClip : INotifyPropertyChanged
    {
        /// <summary>
        /// Unique ID of this clip.
        /// </summary>
        string Id { get; set; }

        /// <summary>
        /// The type of clip (e.g. video, audio, image).
        /// </summary>
        TrackItemType ItemType { get; set; }

        /// <summary>
        /// Absolute file path.
        /// </summary>
        string FilePath { get; set; }

        /// <summary>
        /// Absolute file path for the thumbnail.
        /// </summary>
        string Thumbnail { get; set; }

        /// <summary>
        /// Gets or sets the clip’s length.
        /// </summary>
        TimeCode Length { get; set; }

        /// <summary>
        /// Gets a value indicating whether this clip has an unlimited length.
        /// </summary>
        bool UnlimitedLength { get; }

        /// <summary>
        /// Gets the file name.
        /// </summary>
        [JsonIgnore]
        string FileName { get; }

        /// <summary>
        /// Determines if this clip is currently selected.
        /// </summary>
        [JsonIgnore]
        bool IsSelected { get; set; }

        [JsonIgnore]
        string TempFramesCacheFile { get; }

        [JsonIgnore]
        List<FrameCache> FramesCache { get; }

        bool IsCompatibleWith(TimelineTrackType trackType);

        void CacheFrames();

        /// <summary>
        /// Generates a new Id for the clip.
        /// </summary>
        /// <returns>The new Id.</returns>
        string GenerateNewId();
    }
}