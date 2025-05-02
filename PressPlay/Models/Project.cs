using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Timers;
using System.Linq;
using PressPlay.Helpers;

namespace PressPlay.Models
{
    [Description("Project")]
    [DisplayName("Project")]
    [DebuggerDisplay("Project {Id}")]
    public class Project : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString().Replace("-", "").ToLower();
        private double _fps = 25;
        private int _timelineZoom = 1;
        private TimeCode _needlePositionTime = new TimeCode(0, 25);
        private ObservableCollection<Track> _tracks = new ObservableCollection<Track>();
        private ObservableCollection<ProjectClip> _clips = new ObservableCollection<ProjectClip>();
        private bool _isPlaying;
        private string _currentMediaPath;

        public Timeline.TimelineSelectedTool SelectedTool { get; set; }
        public ObservableCollection<MainWindowViewModel.StepOutlineEntry> StepOutlineEntries { get; set; } = new ObservableCollection<MainWindowViewModel.StepOutlineEntry>();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<TimeCode> NeedlePositionTimeChanged;
        // In TrackItem class
        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public double FPS { get => _fps; set { _fps = value; OnPropertyChanged(); } }
        public int TimelineZoom { get => _timelineZoom; set { _timelineZoom = value; OnPropertyChanged(); } }
        public TimeCode NeedlePositionTime { get => _needlePositionTime; set { _needlePositionTime = value; OnPropertyChanged(); } }
        public ObservableCollection<Track> Tracks { get => _tracks; set { _tracks = value; OnPropertyChanged(); } }
        public ObservableCollection<ProjectClip> Clips
        {
            get => _clips;
            set { _clips = value; OnPropertyChanged(); }
        }
        public string CurrentMediaPath
        {
            get => _currentMediaPath;
            set
            {
                _currentMediaPath = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged();
                }
            }
        }

        public Project()
        {
            // Subscribe to changes in the Tracks collection
            Tracks.CollectionChanged += Tracks_CollectionChanged;
        }
        private void Tracks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // When tracks are added or removed, notify property changed
            OnPropertyChanged(nameof(Tracks));

            // If tracks were added, subscribe to their changes
            if (e.NewItems != null)
            {
                foreach (Track track in e.NewItems)
                {
                    track.Changed += Track_Changed;
                }
            }

            // If tracks were removed, unsubscribe from their changes
            if (e.OldItems != null)
            {
                foreach (Track track in e.OldItems)
                {
                    track.Changed -= Track_Changed;
                }
            }
        }

        private void Track_Changed(object sender, EventArgs e)
        {
            // Track has changed, notify observers
            OnPropertyChanged(nameof(Tracks));
        }

        public void Initialize()
        {
            // Initialize all track items.
            foreach (var track in Tracks)
            {
                foreach (var item in track.Items)
                {
                    item.Initialize();
                }
            }
        }

        public void CutItem(ITrackItem item, double timelineFrame)
        {
            // Implement cut logic.
            if (item == null) return;

            var track = Tracks.FirstOrDefault(t => t.Items.Contains(item));
            if (track == null) return;

            // Calculate local frame (relative to the item start)
            double localFrame = timelineFrame - item.Position.TotalFrames;

            // Ensure we're cutting within the item's duration
            if (localFrame <= 0 || localFrame >= item.Duration.TotalFrames) return;

            // Store original end time
            var originalEnd = item.End;

            // Update the end time of the original item
            item.End = new TimeCode((int)(item.Start.TotalFrames + localFrame), item.Start.FPS);

            // Create a new item for the second part
            ITrackItem newItem;
            if (item is TrackItem trackItem)
            {
                newItem = new TrackItem
                {
                    StartTime = trackItem.StartTime + localFrame,
                    Position = new TimeCode((int)timelineFrame, item.Position.FPS),
                    Start = item.End,
                    End = originalEnd,
                    FadeInFrame = item.FadeInFrame,
                    FadeOutFrame = item.FadeOutFrame,
                    FileName = trackItem.FileName,
                    FullPath = trackItem.FullPath,
                    Thumbnail = trackItem.Thumbnail
                };
            }
            else
            {
                // Generic handling if not a TrackItem
                newItem = (ITrackItem)Activator.CreateInstance(item.GetType());
                newItem.Position = new TimeCode((int)timelineFrame, item.Position.FPS);
                newItem.Start = item.End;
                newItem.End = originalEnd;
                newItem.FadeInFrame = item.FadeInFrame;
                newItem.FadeOutFrame = item.FadeOutFrame;
            }

            // Add the new item to the track
            track.Items.Add(newItem);
        }

        public void ResizeItem(ITrackItem item, double timelineFrame)
        {
            // Implement resize logic.
            if (item == null) return;

            var track = Tracks.FirstOrDefault(t => t.Items.Contains(item));
            if (track == null) return;

            // Calculate local frame (relative to the item start)
            double localFrame = timelineFrame - item.Position.TotalFrames;

            // Ensure we're resizing to a valid position
            if (localFrame <= 0) return;

            // Update the end time of the item
            item.End = new TimeCode((int)(item.Start.TotalFrames + localFrame), item.Start.FPS);
        }

        public void RemoveAndAddTrackItem(Track source, Track destination, ITrackItem item)
        {
            if (!source.Items.Contains(item)) return;

            // Remove from source track
            source.Items.Remove(item);

            // Check if item is compatible with destination track
            if (item.IsCompatibleWith(destination.Type.ToString()))
            {
                // Add to destination track
                destination.Items.Add(item);
            }
        }

        public void DeleteSelectedItems()
        {
            // Find all selected items
            var selectedItems = new List<(Track Track, ITrackItem Item)>();

            foreach (var track in Tracks)
            {
                foreach (var item in track.Items.Where(i => i.IsSelected).ToList())
                {
                    selectedItems.Add((track, item));
                }
            }

            // Remove all selected items from their tracks
            foreach (var (track, item) in selectedItems)
            {
                track.Items.Remove(item);
            }

            // If there were items deleted, notify observers
            if (selectedItems.Count > 0)
            {
                OnPropertyChanged(nameof(Tracks));
            }
        }

        public void Paste()
        {
            // Implement paste logic.
            // This will be implemented when we add clipboard support
        }

        public void RaiseNeedlePositionTimeChanged(TimeCode time)
        {
            NeedlePositionTimeChanged?.Invoke(this, time);
            OnPropertyChanged(nameof(NeedlePositionTime));
        }

        // Helper method to get visible items at a specific frame
        public List<ITrackItem> GetItemsAtFrame(int frame)
        {
            return Tracks
                .SelectMany(t => t.Items)
                .Where(i => i.Position.TotalFrames <= frame &&
                           (i.Position.TotalFrames + i.Duration.TotalFrames) >= frame)
                .ToList();
        }

        // Get the total duration of the project (rightmost item end)
        public double GetTotalDuration()
        {
            if (!Tracks.Any() || !Tracks.SelectMany(t => t.Items).Any())
                return 0;

            return Tracks
                .SelectMany(t => t.Items)
                .Max(i => i.Position.TotalFrames + i.Duration.TotalFrames);
        }

        // Check if items would overlap and prevent it
        public bool WouldItemsOverlap(Track track, ITrackItem newItem, ITrackItem excludeItem = null)
        {
            if (track == null || newItem == null)
                return false;

            double newStart = newItem.Position.TotalFrames;
            double newEnd = newItem.Position.TotalFrames + newItem.Duration.TotalFrames;

            foreach (var existingItem in track.Items)
            {
                // Skip the item we're checking against (useful for move operations)
                if (existingItem == excludeItem)
                    continue;

                double existingStart = existingItem.Position.TotalFrames;
                double existingEnd = existingItem.Position.TotalFrames + existingItem.Duration.TotalFrames;

                // Check for overlap
                if (newStart < existingEnd && newEnd > existingStart)
                {
                    return true;
                }
            }

            return false;
        }

        // Find a valid position to place an item on a track (no overlap)
        public double FindValidPosition(Track track, ITrackItem item)
        {
            if (track == null || item == null)
                return 0;

            // Start with the desired position
            double position = item.Position.TotalFrames;
            double duration = item.Duration.TotalFrames;
            bool foundValid = false;

            while (!foundValid)
            {
                foundValid = true;

                foreach (var existingItem in track.Items)
                {
                    double existingStart = existingItem.Position.TotalFrames;
                    double existingEnd = existingItem.Position.TotalFrames + existingItem.Duration.TotalFrames;

                    // Check if current position would overlap
                    if (position < existingEnd && position + duration > existingStart)
                    {
                        // Move to the end of this item
                        position = existingEnd;
                        foundValid = false;
                        break;
                    }
                }
            }

            return position;
        }
        public ObservableCollection<object> PreviewRenderedTranslations { get; set; } = new ObservableCollection<object>();
        public string PreviewRenderedTranslation { get; set; }
        public double TrackHeadersWidth { get; set; } = 100; // Default width for track headers
        public void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        // Add these properties and methods
        public int FrameInterval => (int)(1000 / FPS); // milliseconds per frame

        public double GetTotalFrames()
        {
            if (Tracks.Count == 0 || !Tracks.SelectMany(t => t.Items).Any())
                return 0;

            return Tracks
                .SelectMany(t => t.Items)
                .Max(i => i.Position.TotalFrames + i.Duration.TotalFrames);
        }
        public ProjectClip GetClipAt(TimeCode time)
        {
            // 1. Turn the TimeCode into a zero-based frame index
            int frame = (int)Math.Round((double)time.TotalFrames);

            // 2. Find all track items that span this frame
            var items = GetItemsAtFrame(frame);
            if (items == null || items.Count == 0)
                return null;

            // 3. Pick the first (or topmost) one
            var item = items.First();

            // 4. Match it to one of your loaded ProjectClips by file path
            //    (assumes your ITrackItem has a FullPath property)
            var clip = Clips
               .FirstOrDefault(c =>
                   string.Equals(c.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase));

            return clip;
        }
        public void Cut()
        {
            // Implementation for Cut operation
            var selectedItems = Tracks
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .ToList();

            // Example implementation - copy items to clipboard
            // Then delete them from timeline
            DeleteSelectedItems();
        }

        public void Copy()
        {
            // Implementation for Copy operation
            var selectedItems = Tracks
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .ToList();

            // Example implementation - copy items to clipboard
        }
    }
}