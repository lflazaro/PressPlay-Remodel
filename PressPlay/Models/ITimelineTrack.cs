using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PressPlay.Models
{
    /// <summary>
    /// Represents a timeline track that contains track items.
    /// </summary>
    public interface ITimelineTrack : INotifyPropertyChanged
    {
        /// <summary>
        /// Occurs when the track is changed.
        /// </summary>
        event EventHandler Changed;

        /// <summary>
        /// The Id of the timeline track.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The name of the timeline track.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The type of the timeline track.
        /// </summary>
        TimelineTrackType Type { get; set; }

        /// <summary>
        /// The height of the track.
        /// </summary>
        int Height { get; set; }

        /// <summary>
        /// Gets or sets the collection of track items.
        /// </summary>
        ObservableCollection<ITrackItem> Items { get; set; }

        /// <summary>
        /// Adds a track item to the track.
        /// </summary>
        void AddTrackItem(ITrackItem item);

        /// <summary>
        /// Adds a collection of track items to the track.
        /// </summary>
        void AddTrackItems(IEnumerable<ITrackItem> items);

        /// <summary>
        /// Removes a track item from the track.
        /// </summary>
        void RemoveTrackItem(ITrackItem item);

        /// <summary>
        /// Removes a collection of track items from the track.
        /// </summary>
        void RemoveTrackItems(IEnumerable<ITrackItem> items);

        /// <summary>
        /// Gets the duration of the track (typically the maximum of its items’ right times).
        /// </summary>
        TimeCode GetDuration();

        /// <summary>
        /// Cuts the item at the specified timeline frame.
        /// </summary>
        void CutItem(ITrackItem item, double timelineFrame);

        /// <summary>
        /// Gets the track item at the specified timeline frame.
        /// </summary>
        ITrackItem GetItemAtTimelineFrame(double timelineFrame);

        /// <summary>
        /// Generates a new Id for the track.
        /// </summary>
        string GenerateNewId();
    }
}