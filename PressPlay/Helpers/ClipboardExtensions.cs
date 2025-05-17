using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using PressPlay.Models;
using PressPlay.Undo;
using PressPlay.Undo.UndoUnits;

namespace PressPlay.Helpers
{
    public static class ClipboardExtensions
    {
        public const string CLIPBOARD_FORMAT = "PressPlay_TrackItems";

        public static void CopySelectedItemsToClipboard(this MainWindowViewModel vm)
        {
            var selected = vm.CurrentProject.Tracks
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .OrderBy(i => i.Start.TotalFrames)
                .ToList();
            if (!selected.Any())
                return;

            // Compute the "anchor" for relative offsets
            int minFrame = selected.Min(i => i.Start.TotalFrames);

            // Build DTOs
            var items = selected.Select(item =>
            {
                var track = vm.CurrentProject.Tracks.First(t => t.Items.Contains(item));

                return new TrackItemData
                {
                    TrackName = track.Name,
                    TrackType = track.Type,
                    FilePath = item.FilePath,
                    ClipId = (item as AudioTrackItem)?.ClipId,

                    // native in-clip start + fps
                    NativeStart = item.Start.TotalFrames,
                    NativeFps = item.Start.FPS,

                    // relative offset & duration in native frames
                    StartOffset = item.Start.TotalFrames - minFrame,
                    Duration = item.Duration.TotalFrames,

                    // fades
                    FadeInFrame = item.FadeInFrame,
                    FadeOutFrame = item.FadeOutFrame,
                    FadeColor = item.FadeColor,

                    // transforms & volume
                    TranslateX = (item as TrackItem)?.TranslateX ?? 0,
                    TranslateY = (item as TrackItem)?.TranslateY ?? 0,
                    ScaleX = (item as TrackItem)?.ScaleX ?? 1,
                    ScaleY = (item as TrackItem)?.ScaleY ?? 1,
                    Rotation = (item as TrackItem)?.Rotation ?? 0,
                    Opacity = (item as TrackItem)?.Opacity ?? 1,
                    Volume = (item as AudioTrackItem)?.Volume
                                 ?? (item as TrackItem)?.Volume
                                 ?? 1,

                    ItemType = item.GetType().Name
                };
            }).ToList();

            var data = new TrackItemClipboardData
            {
                Timestamp = DateTime.Now,
                Items = items
            };

            // Serialize enums by name
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                WriteIndented = false
            };
            string json = JsonSerializer.Serialize(data, options);

            // Build a DataObject so it survives across processes
            var dobj = new DataObject();
            dobj.SetData(CLIPBOARD_FORMAT, json);
            dobj.SetData(DataFormats.UnicodeText, json);

            Clipboard.SetDataObject(dobj, true);
            Debug.WriteLine($"Copied {items.Count} items (anchor frame {minFrame})");
        }
        public static void ClearClipboard()
        {
            try
            {
                // Only clear our specific format, not the entire clipboard
                if (Clipboard.ContainsData(CLIPBOARD_FORMAT))
                {
                    // Create an empty DataObject
                    var dataObj = new DataObject();

                    // Set clipboard to empty object to clear it
                    Clipboard.SetDataObject(dataObj, false);

                    Debug.WriteLine("Cleared PressPlay data from clipboard");
                }
            }
            catch (Exception ex)
            {
                // Just log the error, don't show a message box to avoid interrupting shutdown
                Debug.WriteLine($"Error clearing clipboard: {ex.Message}");
            }
        }
        public static void PasteItemsFromClipboard(this MainWindowViewModel vm)
        {
            var dobj = Clipboard.GetDataObject();
            if (dobj == null || !dobj.GetDataPresent(CLIPBOARD_FORMAT))
            {
                Debug.WriteLine("No PressPlay data in clipboard");
                return;
            }

            try
            {
                var json = dobj.GetData(CLIPBOARD_FORMAT) as string;
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                };
                var clipData = JsonSerializer.Deserialize<TrackItemClipboardData>(json, options);
                if (clipData?.Items?.Count == 0)
                    return;

                int pasteFrame = vm.CurrentProject.NeedlePositionTime.TotalFrames;
                double projFps = vm.CurrentProject.FPS;

                var multiUndo = new MultipleUndoUnits("Paste Items");
                int count = 0;

                foreach (var item in clipData.Items)
                {
                    // find or add the ProjectClip
                    var clip = vm.CurrentProject.Clips
                        .FirstOrDefault(c => !string.IsNullOrEmpty(item.ClipId)
                            ? c.Id == item.ClipId
                            : string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (clip == null)
                    {
                        Debug.WriteLine($"Missing clip for {item.FilePath} (or id {item.ClipId})");
                        continue;
                    }

                    // find or create the Track
                    var track = vm.CurrentProject.Tracks
                        .FirstOrDefault(t => t.Type == item.TrackType && t.Name == item.TrackName)
                                 ?? vm.CurrentProject.Tracks
                                    .FirstOrDefault(t => t.Type == item.TrackType);
                    if (track == null)
                    {
                        track = new Track
                        {
                            Name = $"{item.TrackType} {vm.CurrentProject.Tracks.Count + 1}",
                            Type = item.TrackType
                        };
                        vm.CurrentProject.Tracks.Add(track);
                        multiUndo.UndoUnits.Add(
                            new TrackAddUndoUnit(vm.CurrentProject, track, vm.CurrentProject.Tracks.Count - 1));
                    }

                    // reconstruct start & length in project timebase
                    int startFrame = pasteFrame + item.StartOffset;
                    var sourceStart = new TimeCode(item.NativeStart, item.NativeFps);
                    var length = new TimeCode(item.Duration, item.NativeFps);

                    ITrackItem newItem;
                    if (item.ItemType == nameof(AudioTrackItem)
                     && clip.TrackType == TimelineTrackType.Audio)
                    {
                        newItem = new AudioTrackItem(
                            clip,
                            new TimeCode(startFrame, projFps),
                            sourceStart,
                            length)
                        {
                            FadeInFrame = item.FadeInFrame,
                            FadeOutFrame = item.FadeOutFrame,
                            FadeColor = item.FadeColor,
                            Volume = item.Volume
                        };
                    }
                    else
                    {
                        newItem = new TrackItem(
                            clip,
                            new TimeCode(startFrame, projFps),
                            sourceStart,
                            length)
                        {
                            FadeInFrame = item.FadeInFrame,
                            FadeOutFrame = item.FadeOutFrame,
                            FadeColor = item.FadeColor,
                            TranslateX = item.TranslateX,
                            TranslateY = item.TranslateY,
                            ScaleX = item.ScaleX,
                            ScaleY = item.ScaleY,
                            Rotation = item.Rotation,
                            Opacity = item.Opacity,
                            Volume = item.Volume
                        };
                    }

                    track.AddTrackItem(newItem);
                    multiUndo.UndoUnits.Add(new TrackItemAddUndoUnit(track, newItem));
                    count++;
                }

                if (count > 0)
                {
                    UndoEngine.Instance.AddUndoUnit(multiUndo);
                    vm.HasUnsavedChanges = true;
                    Debug.WriteLine($"Pasted {count} items at frame {pasteFrame}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error pasting from clipboard: {ex}");
                MessageBox.Show(
                    "Error pasting items:\n" + ex.Message,
                    "Paste Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    public class TrackItemClipboardData
    {
        public DateTime Timestamp { get; set; }
        public List<TrackItemData> Items { get; set; } = new();
    }

    public class TrackItemData
    {
        public string TrackName { get; set; }
        public TimelineTrackType TrackType { get; set; }
        public string FilePath { get; set; }
        public string ClipId { get; set; }
        public int StartOffset { get; set; }
        public int Duration { get; set; }
        public int NativeStart { get; set; }
        public double NativeFps { get; set; }
        public int FadeInFrame { get; set; }
        public int FadeOutFrame { get; set; }
        public Track.FadeColor FadeColor { get; set; }
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public double Rotation { get; set; }
        public double Opacity { get; set; }
        public float Volume { get; set; }
        public string ItemType { get; set; }
    }
}
