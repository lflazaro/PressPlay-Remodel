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
using PressPlay.Undo.UndoUnits;
using PressPlay.Undo;

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
        [JsonInclude]
        public int ProjectWidth { get; private set; }
        [JsonInclude]
        public int ProjectHeight { get; private set; }

        // Call this when you first add a clip to the timeline:
        public void SetProjectResolution(int width, int height)
        {
            if (ProjectWidth == 0 && ProjectHeight == 0)
            {
                ProjectWidth = width;
                ProjectHeight = height;
                OnPropertyChanged(nameof(ProjectWidth));
                OnPropertyChanged(nameof(ProjectHeight));
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
            // Check if item exists and is valid
            if (item == null) return;

            var track = Tracks.FirstOrDefault(t => t.Items.Contains(item));
            if (track == null) return;

            // Calculate local frame (relative to the item's position on timeline)
            double localFrame = timelineFrame - item.Position.TotalFrames;

            // Ensure we're cutting within the item's duration
            if (localFrame <= 0 || localFrame >= item.Duration.TotalFrames)
            {
                Debug.WriteLine("Cannot cut - position is outside clip boundaries");
                return;
            }

            Debug.WriteLine($"Cutting clip: Start={item.Start.TotalFrames}, Position={item.Position.TotalFrames}, " +
                           $"End={item.End.TotalFrames}, Duration={item.Duration.TotalFrames}, Cut at local frame={localFrame}");

            // Store original values for undo and for creating the second clip
            var originalEnd = item.End;
            var originalSourceLength = item.SourceLength;
            var originalUnlimitedSourceLength = item.UnlimitedSourceLength;

            // Set up undo tracking before making changes
            var multiUndo = new MultipleUndoUnits("Cut Item");

            // Track the resize undo for the first part
            var resizeData = new TrackItemResizeData(item,
                                                  item.Position,
                                                  item.Start,
                                                  item.End);

            // Update the end time of the original item
            item.End = new TimeCode((int)(item.Start.TotalFrames + localFrame), item.Start.FPS);

            // Update resize data with new values
            resizeData.NewPosition = item.Position;
            resizeData.NewStart = item.Start;
            resizeData.NewEnd = item.End;

            multiUndo.UndoUnits.Add(new TrackItemResizeUndoUnit(resizeData));

            // Calculate correct frame position and time values for the second part
            int newStartFrame = item.Start.TotalFrames + (int)localFrame;
            int newPositionFrame = (int)timelineFrame; // Position on timeline where the cut occurred

            // Create appropriate ITrackItem based on type
            ITrackItem newItem;

            if (item is TrackItem trackItem)
            {
                newItem = new TrackItem
                {
                    StartTime = trackItem.StartTime + localFrame,
                    Position = new TimeCode(newPositionFrame, item.Position.FPS),
                    Start = new TimeCode(newStartFrame, item.Start.FPS),
                    End = originalEnd,
                    FadeInFrame = 0, // Reset fade-in for new clip
                    FadeOutFrame = item.FadeOutFrame, // Keep fade-out from original
                    FileName = trackItem.FileName,
                    FullPath = trackItem.FullPath,
                    FilePath = trackItem.FilePath,
                    Thumbnail = trackItem.Thumbnail,
                    Volume = trackItem.Volume, // Copy volume setting

                    // FIX: Make sure the second half has correct source length
                    SourceLength = originalSourceLength,

                    // FIX: Properly set OriginalEnd to allow resizing
                    OriginalEnd = originalEnd
                };

                // FIX: Copy other transform properties
                if (trackItem is TrackItem ti)
                {
                    ((TrackItem)newItem).TranslateX = ti.TranslateX;
                    ((TrackItem)newItem).TranslateY = ti.TranslateY;
                    ((TrackItem)newItem).ScaleX = ti.ScaleX;
                    ((TrackItem)newItem).ScaleY = ti.ScaleY;
                    ((TrackItem)newItem).Rotation = ti.Rotation;
                    ((TrackItem)newItem).Opacity = ti.Opacity;
                }
            }
            else if (item is AudioTrackItem audioItem)
            {
                // Handle audio items specifically
                var audioClip = Clips.FirstOrDefault(c => c.Id == audioItem.ClipId) as ProjectClip;

                if (audioClip != null)
                {
                    newItem = new AudioTrackItem(
                        audioClip,
                        new TimeCode(newPositionFrame, item.Position.FPS),
                        new TimeCode(newStartFrame, item.Start.FPS),
                        new TimeCode(originalEnd.TotalFrames - newStartFrame, originalEnd.FPS)
                    )
                    {
                        Volume = audioItem.Volume
                    };

                    // FIX: Make sure the second half has correct source length
                    newItem.SourceLength = originalSourceLength;
                    newItem.OriginalEnd = originalEnd;

                    // Add debugging info
                    Debug.WriteLine($"Created AudioTrackItem: Start={newStartFrame}, Position={newPositionFrame}, " +
                                   $"End={originalEnd.TotalFrames}, Duration={(originalEnd.TotalFrames - newStartFrame)}");
                }
                else
                {
                    Debug.WriteLine("Could not find audio clip - aborting cut");
                    return;
                }
            }
            else
            {
                // Generic handling for other item types
                Debug.WriteLine("Unknown track item type - using generic approach");
                newItem = (ITrackItem)Activator.CreateInstance(item.GetType());
                newItem.Position = new TimeCode(newPositionFrame, item.Position.FPS);
                newItem.Start = new TimeCode(newStartFrame, item.Start.FPS);
                newItem.End = originalEnd;
                newItem.FadeInFrame = 0;
                newItem.FadeOutFrame = item.FadeOutFrame;

                // FIX: Always copy these critical properties
                newItem.SourceLength = originalSourceLength;
                newItem.OriginalEnd = originalEnd;
            }

            // Verify the new item has valid duration
            if (newItem.Duration.TotalFrames <= 0)
            {
                Debug.WriteLine("Error: New item has invalid duration. Aborting cut.");
                return;
            }

            // Add the new item to the track
            track.Items.Add(newItem);

            // Add track item add undo
            multiUndo.UndoUnits.Add(new TrackItemAddUndoUnit(track, newItem));

            // Register the undo unit
            UndoEngine.Instance.AddUndoUnit(multiUndo);

            Debug.WriteLine($"Cut complete: Original item new end={item.End.TotalFrames}, " +
                           $"New item: Start={newItem.Start.TotalFrames}, Position={newItem.Position.TotalFrames}, " +
                           $"End={newItem.End.TotalFrames}, Duration={newItem.Duration.TotalFrames}, " +
                           $"OriginalEnd={newItem.OriginalEnd?.TotalFrames}");
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
        public (ProjectClip clip, TimeSpan clipOffset) GetClipAtWithOffset(TimeCode time, Track track)
        {
            // 1) Which frame is the playhead on?
            int frame = (int)Math.Round((double)time.TotalFrames);

            // 2) Find the track‐item in this track that spans that frame
            var item = track.Items.FirstOrDefault(i =>
                i.Position.TotalFrames <= frame &&
                frame < i.Position.TotalFrames + i.Duration.TotalFrames);

            if (item == null)
                return (null, TimeSpan.Zero);

            // 3) Find the matching ProjectClip
            var clip = Clips.FirstOrDefault(c =>
                string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                c.Id == (item as AudioTrackItem)?.ClipId);

            if (clip == null)
                return (null, TimeSpan.Zero);

            // 4) Compute how many frames into the clip we are
            int clipRelativeFrame = frame - item.Position.TotalFrames + item.Start.TotalFrames;
            clipRelativeFrame = Math.Max(0, Math.Min(clipRelativeFrame, (int)clip.Length.TotalFrames - 1));

            // 5) Turn that into a TimeSpan offset
            var clipOffset = TimeSpan.FromSeconds(clipRelativeFrame / clip.FPS);

            return (clip, clipOffset);
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