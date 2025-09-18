using PressPlay.Models;
using System.Collections.Generic;

namespace PressPlay.Undo.UndoUnits
{
    public class TrackItemResizeData
    {
        public ITrackItem Item { get; set; }

        public TimeCode OldStart { get; set; }

        public TimeCode NewStart { get; set; }

        public TimeCode OldEnd { get; set; }

        public TimeCode NewEnd { get; set; }

        public TimeCode OldPosition { get; set; }

        public TimeCode NewPosition { get; set; }

        public Dictionary<Keyframe, int> OldKeyframeFrames { get; } = new();

        public Dictionary<Keyframe, int> NewKeyframeFrames { get; } = new();

        public TrackItemResizeData()
        {
        }

        public TrackItemResizeData(ITrackItem item)
        {
            Item = item;
        }

        public TrackItemResizeData(ITrackItem item, TimeCode oldPosition, TimeCode oldStart, TimeCode oldEnd)
        {
            Item = item;
            OldPosition = oldPosition;
            OldStart = oldStart;
            OldEnd = oldEnd;

            // Set the new values to the old values, so if the undo is called, it will revert to the original values.
            NewPosition = item.Position;
            NewStart = item.Start;
            NewEnd = item.End;

            if (item is TrackItem trackItem)
            {
                CaptureKeyframeFrames(trackItem, OldKeyframeFrames);
                CaptureKeyframeFrames(trackItem, NewKeyframeFrames);
            }
        }

        public void Undo()
        {
            Item.Start = OldStart;
            Item.End = OldEnd;
            Item.Position = OldPosition;

            if (Item is TrackItem trackItem)
            {
                ApplyKeyframeFrames(trackItem, OldKeyframeFrames);
            }
        }

        public void Redo()
        {
            Item.Start = NewStart;
            Item.End = NewEnd;
            Item.Position = NewPosition;

            if (Item is TrackItem trackItem)
            {
                ApplyKeyframeFrames(trackItem, NewKeyframeFrames);
            }
        }

        public void SetNewKeyframeFrames(TrackItem trackItem)
        {
            CaptureKeyframeFrames(trackItem, NewKeyframeFrames);
        }

        private static void CaptureKeyframeFrames(TrackItem trackItem, Dictionary<Keyframe, int> target)
        {
            target.Clear();
            foreach (var collection in trackItem.Keyframes.Values)
            {
                foreach (var keyframe in collection)
                {
                    target[keyframe] = keyframe.Frame;
                }
            }
        }

        private static void ApplyKeyframeFrames(TrackItem trackItem, Dictionary<Keyframe, int> frames)
        {
            if (frames.Count == 0)
            {
                return;
            }

            foreach (var collection in trackItem.Keyframes.Values)
            {
                foreach (var keyframe in collection)
                {
                    if (frames.TryGetValue(keyframe, out int frame))
                    {
                        keyframe.Frame = frame;
                    }
                }
            }
        }
    }
}
