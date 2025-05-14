using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PressPlay.Effects;
using PressPlay.Models;
using static PressPlay.Models.Track;  // your enum lives here

namespace PressPlay.Models
{
    public interface ITrackItem
    {
        // Core properties
        double StartTime { get; set; }
        TimeCode Position { get; set; }
        TimeCode Start { get; set; }
        TimeCode End { get; set; }
        TimeCode Duration { get; }
        TimeCode OriginalEnd { get; set; } // Added this property

        // Visual properties
        int FadeInFrame { get; set; }
        int FadeOutFrame { get; set; }
        bool IsSelected { get; set; }
        bool IsChangingFadeIn { get; }
        bool IsChangingFadeOut { get; }
        bool UnlimitedSourceLength { get; }

        // Media properties
        string FileName { get; set; }
        string FilePath { get; set; }
        string FullPath { get; set; }
        byte[] Thumbnail { get; set; }

        TimeCode SourceLength { get; set; }
        Track.FadeColor FadeColor { get; set; } // Removed initializer

        // Methods
        bool IsCompatibleWith(string trackType);
        void Initialize();
        double GetScaledPosition(int zoomLevel);
        double GetScaledWidth(int zoomLevel);
    }
    public class Track : ITimelineTrack
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Changed;

        private string _name;
        private TimelineTrackType _type = TimelineTrackType.Video;
        private ObservableCollection<ITrackItem> _items = new ObservableCollection<ITrackItem>();
        private int _height = 100;
        private string _id = Guid.NewGuid().ToString();

        public string Id => _id;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimelineTrackType Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged();
                }
            }
        }
        public enum FadeColor
        {
            Black,  // the default: fade the clip’s opacity over your black background
            White   // new: fade to/from white
        }

        public int Height
        {
            get => _height;
            set
            {
                if (_height != value)
                {
                    _height = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<ITrackItem> Items
        {
            get => _items;
            set
            {
                if (_items != value)
                {
                    if (_items != null)
                    {
                        _items.CollectionChanged -= Items_CollectionChanged;
                    }

                    _items = value;

                    if (_items != null)
                    {
                        _items.CollectionChanged += Items_CollectionChanged;
                    }

                    OnPropertyChanged();
                }
            }
        }

        public Track()
        {
            if (_items != null)
            {
                _items.CollectionChanged += Items_CollectionChanged;
            }
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Log changes to help with debugging
            System.Diagnostics.Debug.WriteLine($"Track '{Name}' items changed: {e.Action}");

            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (ITrackItem item in e.NewItems)
                {
                    System.Diagnostics.Debug.WriteLine($"Item added at position {item.Position.TotalFrames}");
                }
            }

            OnChanged();
            OnPropertyChanged(nameof(Items));
        }

        public void AddTrackItem(ITrackItem item)
        {
            Items.Add(item);
        }

        public void AddTrackItems(System.Collections.Generic.IEnumerable<ITrackItem> items)
        {
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }

        public void RemoveTrackItem(ITrackItem item)
        {
            Items.Remove(item);
        }

        public void RemoveTrackItems(System.Collections.Generic.IEnumerable<ITrackItem> items)
        {
            foreach (var item in items)
            {
                Items.Remove(item);
            }
        }

        public TimeCode GetDuration()
        {
            if (Items.Count == 0)
            {
                return TimeCode.Zero;
            }

            int maxFrame = 0;
            double fps = 25.0; // Default FPS

            foreach (var item in Items)
            {
                int endFrame = item.Position.TotalFrames + item.Duration.TotalFrames;
                if (endFrame > maxFrame)
                {
                    maxFrame = endFrame;
                    fps = item.Position.FPS;
                }
            }

            return new TimeCode(maxFrame, fps);
        }

        public void CutItem(ITrackItem item, double timelineFrame)
        {
            // Implementation of cut logic would go here
            System.Diagnostics.Debug.WriteLine($"Cutting item at frame {timelineFrame}");
            OnChanged();
        }

        public ITrackItem GetItemAtTimelineFrame(double timelineFrame)
        {
            foreach (var item in Items)
            {
                int startFrame = item.Position.TotalFrames;
                int endFrame = startFrame + item.Duration.TotalFrames;

                if (timelineFrame >= startFrame && timelineFrame <= endFrame)
                {
                    return item;
                }
            }

            return null;
        }

        public string GenerateNewId()
        {
            _id = Guid.NewGuid().ToString();
            return _id;
        }

        protected void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            OnChanged();
        }

        protected void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}