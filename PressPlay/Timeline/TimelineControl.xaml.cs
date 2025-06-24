using CommunityToolkit.Mvvm.Input;
using PressPlay.Models;
using PressPlay.Helpers;
using PressPlay.Undo;
using PressPlay.Undo.UndoUnits;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.IO;

namespace PressPlay.Timeline
{
    /// <summary>
    /// Interaction logic for TimelineControl.xaml
    /// </summary>
    public partial class TimelineControl : UserControl
    {
        private bool _tracksCanvasLeftMouseButtonDown;
        private TrackItemControl _mouseDownElement;
        private Ellipse _mouseDownFadeControl;
        private double _mouseDownX;
        private double _trackMouseDownX;
        private ITrackItem _mouseDownTrackItem;
        private double _mouseDownFadeIn;
        private double _mouseDownFadeOut;
        private ITimelineTrack _mouseDownTrack;
        private ITimelineTrack _mouseUpTrack;
        private TimeCode _mouseDownTrackItemPosition;
        private TimeCode _mouseDownTrackItemStart;
        private TimeCode _mouseDownTrackItemEnd;
        private bool _resizingLeft;
        private bool _resizingRight;

        public Project Project
        {
            get => (Project)GetValue(ProjectProperty);
            set => SetValue(ProjectProperty, value);
        }

        public static readonly DependencyProperty ProjectProperty =
            DependencyProperty.Register(nameof(Project), typeof(Project), typeof(TimelineControl),
                new PropertyMetadata(null, OnProjectChangedCallback));

        public TimelineControl()
        {
            InitializeComponent();
            tracksHScrollView.ScrollChanged += (s, e) =>
    header.HeaderScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);

            if (Project != null)
                UpdateCursor();
        }

        [RelayCommand]
        private void OnToolSelected(TimelineSelectedTool tool)
        {
            Project.SelectedTool = tool;
        }

        private static void OnProjectChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl control)
            {
                if (e.OldValue is Project oldProject)
                    oldProject.PropertyChanged -= control.Project_PropertyChanged;

                if (e.NewValue is Project newProject)
                {
                    newProject.PropertyChanged -= control.Project_PropertyChanged;
                    newProject.PropertyChanged += control.Project_PropertyChanged;
                }
            }
        }

        private void Project_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Project.TimelineZoom) || e.PropertyName == nameof(Project.NeedlePositionTime))
            {
                double left = Project.NeedlePositionTime.TotalFrames * Constants.TimelinePixelsInSeparator
                              / Constants.TimelineZooms[Project.TimelineZoom];
                Canvas.SetLeft(needle, left);
                needle.BringIntoView();
            }
            else if (e.PropertyName == nameof(Project.SelectedTool))
            {
                OnToolSelected(Project.SelectedTool);
                UpdateCursor();
            }
        }

        private void ScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            tracksHScrollView.ScrollToHorizontalOffset(e.NewValue);
        }
        public static class VisualTreeHelpers
        {
            public static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
            {
                while (current != null)
                {
                    if (current is T match)
                        return match;
                    current = VisualTreeHelper.GetParent(current);
                }
                return null;
            }
        }
        private void TracksCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ITrackItem item)
                item.IsSelected = true;

            UpdateNeedlePosition(e.GetPosition(header).X);
        }
        private void AutoCrossfadeOnOverlap(ITrackItem movedItem, Track track)
        {
            // 1) Gather & sort all items on this track by start frame
            var items = track.Items
                .OrderBy(i => i.Position.TotalFrames)
                .ToList();

            // 2) For each consecutive pair, compute overlap
            for (int k = 0; k < items.Count - 1; k++)
            {
                var a = items[k];
                var b = items[k + 1];

                long aEnd = a.Position.TotalFrames + a.Duration.TotalFrames;
                long bStart = b.Position.TotalFrames;
                long overlap = aEnd - bStart;

                if (overlap > 0)
                {
                    // Overlapping → apply crossfade on both items
                    if (a.FadeColor != Track.FadeColor.White)
                        a.FadeColor = Track.FadeColor.Black;
                    a.FadeOutFrame = (int)overlap;

                    if (b.FadeColor != Track.FadeColor.White)
                        b.FadeColor = Track.FadeColor.Black;
                    b.FadeInFrame = (int)overlap;
                }
                else
                {
                    // No overlap → clear fades only on the moved item, keep FadeColor
                    if (movedItem == a && a.FadeOutFrame > 0)
                        a.FadeOutFrame = 0;

                    if (movedItem == b && b.FadeInFrame > 0)
                        b.FadeInFrame = 0;
                }
            }
        }
        private void TracksCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var over = e.OriginalSource as DependencyObject;
                if (VisualTreeHelpers.FindAncestor<Slider>(over) != null)
                    return;
            }
            Focus();
            if (e.ClickCount > 1) return;
            _trackMouseDownX = e.GetPosition(tracksControl).X;

            // Check if the click is directly on the canvas (not on a clip)
            bool isCanvasClick = true;
            if (e.OriginalSource is FrameworkElement fe)
            {
                // Check if there's a TrackItemControl in the visual tree
                var trackItemControl = VisualHelper.GetAncestor<TrackItemControl>(fe);
                isCanvasClick = trackItemControl == null;
            }

            // If it's a direct click on the canvas, clear all selections
            if (isCanvasClick)
            {
                TrackItemControl.ClearAllSelections();
            }

            // Original code continues here
            if (e.OriginalSource is FrameworkElement originalFe && originalFe.DataContext is ITrackItem item)
            {
                var tic = originalFe as TrackItemControl ?? VisualHelper.GetAncestor<TrackItemControl>(originalFe);
                double mouseX = e.GetPosition(tracksControl).X;
                double itemX = tic.TranslatePoint(new Point(), tracksControl).X;
                double itemRight = itemX + tic.Width;
                double frame = Math.Round(mouseX / Constants.TimelinePixelsInSeparator
                                         * Constants.TimelineZooms[Project.TimelineZoom], MidpointRounding.ToZero);

                _mouseDownElement = tic;
                _mouseDownX = e.GetPosition(_mouseDownElement).X;
                _mouseDownTrackItem = item;
                _mouseDownFadeIn = item.FadeInFrame;
                _mouseDownFadeOut = item.FadeOutFrame;
                _mouseDownTrack = Project.Tracks.First(t => t.Items.Contains(item));
                _mouseDownTrackItemPosition = item.Position;
                _mouseDownTrackItemStart = item.Start;
                _mouseDownTrackItemEnd = item.End;

                if (Project.SelectedTool == TimelineSelectedTool.SelectionTool)
                {
                    if (Math.Abs(mouseX - itemX) <= 3) _resizingLeft = true;
                    else if (Math.Abs(mouseX - itemRight) <= 3) _resizingRight = true;
                    else _mouseDownTrackItem.IsSelected = true;
                }
                else if (Project.SelectedTool == TimelineSelectedTool.CuttingTool)
                {
                    Project.CutItem(item, frame);
                }
                else if (Project.SelectedTool == TimelineSelectedTool.RazorCutTool)
                {
                    // Call the Project's CutItem method with the current item and timeline frame
                    Project.CutItem(item, frame);
                    Debug.WriteLine($"Cutting clip at frame {frame}");
                    e.Handled = true;
                }
            }
            else if (e.OriginalSource is Ellipse fadeControl && fadeControl.DataContext is ITrackItem ti)
            {
                _mouseDownTrackItem = ti;
                _mouseDownTrackItemPosition = ti.Position;
                _mouseDownTrackItemStart = ti.Start;
                _mouseDownTrackItemEnd = ti.End;
                _mouseDownFadeControl = fadeControl;
                _mouseDownElement = VisualHelper.GetAncestor<TrackItemControl>(fadeControl);
            }
            else
            {
                UpdateNeedlePosition(e.GetPosition(header).X);
            }

            _tracksCanvasLeftMouseButtonDown = true;
        }
        private void UpdateCursor()
        {
            switch (Project.SelectedTool)
            {
                case TimelineSelectedTool.SelectionTool:
                    Cursor = Cursors.Arrow;
                    break;
                case TimelineSelectedTool.RazorCutTool:
                    Cursor = Cursors.IBeam;
                    break;
                default:
                    Cursor = Cursors.Arrow;
                    break;
            }
        }
        private void TimelineControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _mouseUpTrack = Project.Tracks.FirstOrDefault(t => t.Items.Contains(_mouseDownTrackItem));
            if (_mouseDownTrackItem != null)
            {
                var data = new TrackItemResizeData(_mouseDownTrackItem,
                                                  _mouseDownTrackItemPosition,
                                                  _mouseDownTrackItemStart,
                                                  _mouseDownTrackItemEnd)
                {
                    NewPosition = _mouseDownTrackItem.Position,
                    NewStart = _mouseDownTrackItem.Start,
                    NewEnd = _mouseDownTrackItem.End
                };

                var multi = new MultipleUndoUnits();
                multi.UndoUnits.Add(new TrackItemResizeUndoUnit(data));

                if (_mouseDownTrack != _mouseUpTrack)
                {
                    var rem = new TrackItemRemoveUndoUnit();
                    rem.Items.Add(new TrackAndItemData(_mouseDownTrack, _mouseDownTrackItem));
                    multi.UndoUnits.Add(rem);

                    var add = new TrackItemAddUndoUnit(_mouseUpTrack, _mouseDownTrackItem);
                    multi.UndoUnits.Add(add);
                }

                if (_mouseDownTrackItem.FadeInFrame != _mouseDownFadeIn
                    || _mouseDownTrackItem.FadeOutFrame != _mouseDownFadeOut)
                {
                    multi.UndoUnits.Add(new TrackItemFadeUndoUnit(_mouseDownTrackItem,
                                                                  _mouseDownFadeIn,
                                                                  _mouseDownFadeOut)
                    {
                        NewFadeInFrame = _mouseDownTrackItem.FadeInFrame,
                        NewFadeOutFrame = _mouseDownTrackItem.FadeOutFrame
                    });
                }

                UndoEngine.Instance.AddUndoUnit(multi);
                if (_mouseUpTrack is Track track)
                {
                    AutoCrossfadeOnOverlap(_mouseDownTrackItem, track);
                }
            }

            _mouseDownElement = null;
            _mouseDownTrackItem = null;
            _resizingLeft = false;
            _resizingRight = false;
            _tracksCanvasLeftMouseButtonDown = false;
        }
        private Track GetTrackAtPosition(Point position)
        {
            // Handle by vertical position (Y coordinate)
            double accumulatedHeight = 30; // Account for the header height

            foreach (var track in Project.Tracks)
            {
                double trackHeight = track.Height;
                if (position.Y >= accumulatedHeight &&
                    position.Y < accumulatedHeight + trackHeight)
                {
                    return track as Track;
                }
                accumulatedHeight += trackHeight;
            }

            // If we're here, we might be below all tracks or in an invalid area
            return null;
        }


        private int GetSnappedFrame(int frame)
        {
            if (!Project.MagnetEnabled)
                return frame;

            const int threshold = 25;
            var snapPoints = Project.Tracks
                .SelectMany(t => t.Items)
                .SelectMany(i => new[]
                {
                    i.Position.TotalFrames,
                    i.End.TotalFrames
                })
                .Distinct();

            foreach (var point in snapPoints)
            {
                if (Math.Abs(frame - point) <= threshold)
                    return point;
            }

            return frame;
        }
        private void TimelineControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_tracksCanvasLeftMouseButtonDown && _mouseDownTrackItem != null
    && !_resizingLeft && !_resizingRight
    && !_mouseDownTrackItem.IsChangingFadeIn
    && !_mouseDownTrackItem.IsChangingFadeOut)
            {
                // Get mouse position relative to the tracks canvas
                double mouseX = e.GetPosition(tracksControl).X;

                // Calculate movement delta from the starting position
                double mouseDeltaX = mouseX - _trackMouseDownX;

                // Convert pixel delta to frames accurately
                double pixelsPerFrame = Constants.TimelinePixelsInSeparator / Constants.TimelineZooms[Project.TimelineZoom];
                int frameDelta = (int)Math.Round(mouseDeltaX / pixelsPerFrame);

                // Calculate new position based on the original position
                int newPositionFrames = _mouseDownTrackItemPosition.TotalFrames + frameDelta;
                if (newPositionFrames < 0) newPositionFrames = 0;
                newPositionFrames = GetSnappedFrame(newPositionFrames);

                // Update track item position
                _mouseDownTrackItem.Position = new TimeCode(newPositionFrames, Project.FPS);

                var originTrack = Project.Tracks.First(t => t.Items.Contains(_mouseDownTrackItem));
                var mousePosition = e.GetPosition(tracksControl.Parent as IInputElement);
                var destTrack = GetTrackAtPosition(mousePosition);

                // Move to different track if compatible
                if (originTrack != destTrack && destTrack != null &&
                    _mouseDownTrackItem.IsCompatibleWith(destTrack.Type.ToString()))
                {
                    Project.RemoveAndAddTrackItem((Track)originTrack, destTrack, _mouseDownTrackItem);
                }
            }
            else if (_tracksCanvasLeftMouseButtonDown
         && _mouseDownTrackItem != null
         && _resizingLeft)
            {
                // 1) compute frames under the mouse
                double x = e.GetPosition(tracksControl).X;
                double itemX = _mouseDownElement.TranslatePoint(new Point(), tracksControl).X;
                double frame0 = Math.Round(itemX
                                    / Constants.TimelinePixelsInSeparator
                                    * Constants.TimelineZooms[Project.TimelineZoom],
                                    MidpointRounding.ToZero);
                double currentFr = Math.Round(x
                                    / Constants.TimelinePixelsInSeparator
                                    * Constants.TimelineZooms[Project.TimelineZoom],
                                    MidpointRounding.ToZero);

                // 2) how many frames we've moved
                int diff = (int)(frame0 - currentFr);

                // 3) propose a new Start frame
                int desiredStart = _mouseDownTrackItemStart.TotalFrames + diff;

                // 4) clamp between zero and one less than the End
                desiredStart = Math.Max(0, desiredStart);
                desiredStart = Math.Min(desiredStart, _mouseDownTrackItemEnd.TotalFrames - 1);
                desiredStart = GetSnappedFrame(desiredStart);

                // 5) apply it
                _mouseDownTrackItem.Start = new TimeCode(desiredStart, Project.FPS);
            }

            else if (_tracksCanvasLeftMouseButtonDown && _mouseDownTrackItem != null && _resizingRight)
            {
                double x = e.GetPosition(tracksControl).X;
                double itemX = _mouseDownElement.TranslatePoint(new Point(), tracksControl).X;
                double frame0 = Math.Round(itemX / Constants.TimelinePixelsInSeparator
                                           * Constants.TimelineZooms[Project.TimelineZoom],
                                           MidpointRounding.ToZero);
                double currentFrame = Math.Round(x / Constants.TimelinePixelsInSeparator
                                                * Constants.TimelineZooms[Project.TimelineZoom],
                                                MidpointRounding.ToZero);
                int diff = (int)(currentFrame - frame0);

                // Calculate desired end frame based on mouse position
                int desiredEndFrame = _mouseDownTrackItemStart.TotalFrames + diff;

                // For media with limited source length (audio/video), cap at original length
                if (!_mouseDownTrackItem.UnlimitedSourceLength)
                {
                    // Get max possible end frame (original source length)
                    int maxEndFrame = _mouseDownTrackItem.OriginalEnd != null
                        ? _mouseDownTrackItem.OriginalEnd.TotalFrames
                        : _mouseDownTrackItem.End.TotalFrames;

                    // Ensure we don't exceed the original media length
                    desiredEndFrame = Math.Min(desiredEndFrame, maxEndFrame);
                }

                // Ensure we don't make the clip shorter than 1 frame
                desiredEndFrame = Math.Max(desiredEndFrame, _mouseDownTrackItem.Start.TotalFrames + 1);
                desiredEndFrame = GetSnappedFrame(desiredEndFrame);

                // Set the new End position
                _mouseDownTrackItem.End = new TimeCode(desiredEndFrame, Project.FPS);
            }
            else if (_mouseDownTrackItem != null
                     && (_mouseDownTrackItem.IsChangingFadeIn || _mouseDownTrackItem.IsChangingFadeOut))
            {
                double x = e.GetPosition(_mouseDownElement).X;
                if (_mouseDownTrackItem.IsChangingFadeOut) x -= _mouseDownElement.Width;
                x -= 5;
                int destFrame = Convert.ToInt32(Math.Round(x / Constants.TimelinePixelsInSeparator
                                                         * Constants.TimelineZooms[Project.TimelineZoom],
                                                         MidpointRounding.ToZero));
                if (_mouseDownTrackItem.IsChangingFadeIn)
                {
                    if (destFrame < 0) destFrame = 0;
                    _mouseDownTrackItem.FadeInFrame = destFrame;
                }
                else
                {
                    if (destFrame > 0) destFrame = 0;
                    _mouseDownTrackItem.FadeOutFrame = Math.Abs(destFrame);
                }
                Debug.WriteLine($"changing fade... ({x}, {destFrame})");
            }
            else if (_tracksCanvasLeftMouseButtonDown && _mouseDownElement == null)
            {
                UpdateNeedlePosition(e.GetPosition(header).X);
            }
        }

        private void TracksScrollView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0) Project.TimelineZoom++;
                else Project.TimelineZoom--;
            }
            else tracksHScrollView.ScrollToHorizontalOffset(tracksHScrollView.HorizontalOffset - e.Delta);
        }

        private void UpdateNeedlePosition(double needleXPosition)
        {
            double x = Math.Round(needleXPosition);
            if (x < 0) x = 0;
            double frame = Math.Round(x / Constants.TimelinePixelsInSeparator
                                      * Constants.TimelineZooms[Project.TimelineZoom],
                                      MidpointRounding.ToZero);
            frame = GetSnappedFrame((int)frame);
            double newX = frame * Constants.TimelinePixelsInSeparator
                          / Constants.TimelineZooms[Project.TimelineZoom];

            Canvas.SetLeft(needle, newX);
            Project.NeedlePositionTime = new TimeCode((int)frame, Project.FPS);
            Project.RaiseNeedlePositionTimeChanged(Project.NeedlePositionTime);
        }

        private void Timeline_PreviewDrop(object sender, DragEventArgs e)
        {
            // 1) Pull out the ProjectClip being dragged
            if (!(e.Data.GetData(typeof(ProjectClip)) is ProjectClip clip))
                return;

            // Debug info
            Debug.WriteLine($"Drop detected: Clip={clip.FileName}, Type={clip.TrackType}, ItemType={clip.ItemType}");

            // 2) Compute the timeline frame from X
            //    Use the header control to get X in timeline coords
            double dropX = e.GetPosition(header).X;
            int frame = (int)Math.Round(
                dropX
              / Constants.TimelinePixelsInSeparator
              * Constants.TimelineZooms[Project.TimelineZoom],
              MidpointRounding.ToZero);
            var position = new TimeCode(frame, Project.FPS);

            Debug.WriteLine($"Drop position: X={dropX}, Frame={frame}");

            // 3) Find target track based on position and type
            Track targetTrack = null;

            // For audio clips, we need to create a new or find an empty track
            if (clip.ItemType == TrackItemType.Audio)
            {
                // CHANGE: Find an empty audio track or create a new one
                targetTrack = FindEmptyAudioTrackOrCreateNew();
            }
            else
            {
                // For non-audio clips, use normal track finding logic
                Point canvasPoint = e.GetPosition(RootCanvas);
                double yInTracks = canvasPoint.Y - header.ActualHeight;
                if (yInTracks < 0) yInTracks = 0;

                // Find which track row this Y falls into
                double cumulative = 0;
                foreach (var t in Project.Tracks)
                {
                    cumulative += t.Height;
                    if (yInTracks <= cumulative)
                    {
                        targetTrack = t as Track;
                        break;
                    }
                }
            }

            // Abort if no target track found
            if (targetTrack == null)
            {
                Debug.WriteLine("Drop aborted: No target track found");
                return;
            }

            // 4) Check compatibility
            if (!clip.IsCompatibleWith(targetTrack.Type))
            {
                Debug.WriteLine($"Drop aborted: Incompatible clip ({clip.ItemType}) and track ({targetTrack.Type})");
                return;
            }

            // 5) Create the correct ITrackItem
            ITrackItem ti;
            if (clip.TrackType == TimelineTrackType.Audio)
            {
                Debug.WriteLine("Creating AudioTrackItem");
                {
                    // Compute how many PROJECT frames this clip lasts:
                    double clipSeconds = clip.Length.TotalSeconds;
                    int projFrames = (int)Math.Round(clipSeconds * Project.FPS);
                    var projLength = new TimeCode(projFrames, Project.FPS);

                    ti = new AudioTrackItem(
                            clip,
                            position,                           // position already in project-frames
                            new TimeCode(0, Project.FPS),      // source-start (if you need clip-based, you can keep clip.FPS here)
                            projLength                          // length in project-frames
                        );
                }
            }
            else
            {
                Debug.WriteLine("Creating TrackItem");
                {
                    double clipSeconds = clip.Length.TotalSeconds;
                    int projFrames = (int)Math.Round(clipSeconds * Project.FPS);
                    var projLength = new TimeCode(projFrames, Project.FPS);

                    ti = new TrackItem(
                            clip,
                            position,
                            new TimeCode(0, Project.FPS),
                            projLength
                        );
                }
            }

            // 6) Add it to the track
            targetTrack.AddTrackItem(ti);
            Debug.WriteLine($"Item added to track: {targetTrack.Name}");

            // 7) Optionally, update project resolution if this is a video
            if (clip.TrackType == TimelineTrackType.Video)
                Project.SetProjectResolution(clip.Width, clip.Height);

            e.Handled = true;
        }


        private Track AddAudioTrackAtBottom()
        {
            int audioTrackCount = Project.Tracks.Count(t => t.Type == TimelineTrackType.Audio) + 1;
            var newTrack = new Track
            {
                Name = $"Audio {audioTrackCount}",
                Type = TimelineTrackType.Audio
            };
            Project.Tracks.Add(newTrack);

            // Create undo unit for track addition
            var undoUnit = new TrackAddUndoUnit(Project, newTrack, Project.Tracks.Count - 1);
            UndoEngine.Instance.AddUndoUnit(undoUnit);

            Debug.WriteLine($"Created new audio track: {newTrack.Name}");
            return newTrack;
        }

        private void Timeline_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ProjectClip)) is ProjectClip clip)
            {
                // Always allow audio clips (we'll auto-create a track)
                if (clip.ItemType == TrackItemType.Audio)
                    return;

                // For non-audio, check compatibility with target track
                if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ITimelineTrack tt && clip.IsCompatibleWith(tt.Type))
                    return;
            }

            // Disallow the drop if we got here
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Timeline_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (IsKeyboardFocusWithin && e.Key == Key.Delete)
                Project.DeleteSelectedItems();
        }

        private void PasteItem_Click(object sender, RoutedEventArgs e)
        {
            Project.Paste();
        }
        // New helper method to find an empty audio track or create one
        private Track FindEmptyAudioTrackOrCreateNew()
        {
            // Look for existing audio tracks that have no items
            var emptyAudioTrack = Project.Tracks
                .OfType<Track>()
                .Where(t => t.Type == TimelineTrackType.Audio && t.Items.Count == 0)
                .FirstOrDefault();

            if (emptyAudioTrack != null)
            {
                Debug.WriteLine($"Found empty audio track: {emptyAudioTrack.Name}");
                return emptyAudioTrack;
            }

            // No empty track found, create a new one
            return AddAudioTrackAtBottom();
        }

        private Track EnsureAudioTrackExists()
        {
            // Look for existing audio tracks
            var audioTrack = Project.Tracks.FirstOrDefault(t => t.Type == TimelineTrackType.Audio) as Track;

            // If no audio track exists, create one
            if (audioTrack == null)
            {
                int audioTrackCount = Project.Tracks.Count(t => t.Type == TimelineTrackType.Audio) + 1;
                audioTrack = new Track
                {
                    Name = $"Audio {audioTrackCount}",
                    Type = TimelineTrackType.Audio,
                    Height = (int)Constants.TrackHeight // Use standard track height
                };

                // Add to project tracks collection
                Project.Tracks.Add(audioTrack);

                // Create undo unit for track addition
                var undoUnit = new TrackAddUndoUnit(Project, audioTrack, Project.Tracks.Count - 1);
                UndoEngine.Instance.AddUndoUnit(undoUnit);

                // Log the action
                Debug.WriteLine($"Created new audio track: {audioTrack.Name}");

                // Mark project as having unsaved changes
                if (MainWindowViewModel.Instance != null)
                {
                    MainWindowViewModel.Instance.HasUnsavedChanges = true;
                }
            }

            // Find the track again (in case another thread modified the collection)
            audioTrack = Project.Tracks.FirstOrDefault(t => t.Type == TimelineTrackType.Audio) as Track;

            // If still null (highly unlikely), create one without undo support as a fallback
            if (audioTrack == null)
            {
                audioTrack = new Track
                {
                    Name = "Audio Track",
                    Type = TimelineTrackType.Audio,
                    Height = (int)Constants.TrackHeight
                };
                Project.Tracks.Add(audioTrack);
                Debug.WriteLine("Created fallback audio track");
            }

            return audioTrack;
        }
    }
}