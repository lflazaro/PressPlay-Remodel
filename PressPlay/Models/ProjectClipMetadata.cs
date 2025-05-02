namespace PressPlay.Models
{
    /// <summary>
    /// Represents the metadata of a project clip.
    /// </summary>
    public class ProjectClipMetadata
    {
        public TimelineTrackType TrackType { get; set; }
        public TrackItemType ItemType { get; set; }
        public TimeCode Length { get; set; }
        public double FPS { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool UnlimitedLength { get; set; }
    }
}