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
            }
        }

        private void ScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            tracksHScrollView.ScrollToHorizontalOffset(e.NewValue);
        }

        private void TracksCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is TrackItem item)
                item.IsSelected = true;

            UpdateNeedlePosition(e.GetPosition(header).X);
        }

        private void TracksCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //TODO: Check if the mouse is over a track item or a fade control
            //TODO: We need to fix the selection tool. Right now the only way you can select and move a clip is with the selection tool (which you can switch with the cutting tool using the two radio buttons above)
            //by hovering your mouse over the end of the clip (until the cursor changes to that of a text field cursor) and then dragging it in an X-axis, but I'd like to get rid of the radio buttons altogether and
            //just make it so that you can click anywhere over a clip, select it by visibly changing its background color, and drag it freely across the timeline and maybe even across tracks, while still retaining
            //the cutting functionality in case you want to shorten it by grabbing it from the sides.
            Focus();
            if (e.ClickCount > 1) return;

            _trackMouseDownX = e.GetPosition(tracksControl).X;
            Project.Tracks.SelectMany(t => t.Items).ToList().ForEach(x => x.IsSelected = false);

            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is TrackItem item)
            {
                var tic = fe as TrackItemControl ?? VisualHelper.GetAncestor<TrackItemControl>(fe);
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
            }
            else if (e.OriginalSource is Ellipse fadeControl && fadeControl.DataContext is TrackItem ti)
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
            else if (_tracksCanvasLeftMouseButtonDown && _mouseDownTrackItem != null && _resizingLeft)
            {
                double x = e.GetPosition(tracksControl).X;
                double itemX = _mouseDownElement.TranslatePoint(new Point(), tracksControl).X;
                double frame0 = Math.Round(itemX / Constants.TimelinePixelsInSeparator
                                            * Constants.TimelineZooms[Project.TimelineZoom],
                                            MidpointRounding.ToZero);
                double currentFrame = Math.Round(x / Constants.TimelinePixelsInSeparator
                                                * Constants.TimelineZooms[Project.TimelineZoom],
                                                MidpointRounding.ToZero);
                var diff = frame0 - currentFrame;
                var oldStart = _mouseDownTrackItem.Start;

                if (!_mouseDownTrackItem.UnlimitedSourceLength)
                    _mouseDownTrackItem.Start = new TimeCode(oldStart.TotalFrames - (int)diff, Project.FPS);

                if (_mouseDownTrackItem.UnlimitedSourceLength)
                {
                    _mouseDownTrackItem.Position = new TimeCode(_mouseDownTrackItem.Position.TotalFrames - (int)diff, Project.FPS);
                    _mouseDownTrackItem.End = new TimeCode(_mouseDownTrackItem.End.TotalFrames + (int)diff, Project.FPS);
                }
                else if (oldStart != _mouseDownTrackItem.Start)
                {
                    _mouseDownTrackItem.Position = new TimeCode(_mouseDownTrackItem.Position.TotalFrames - (int)diff, Project.FPS);
                }
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

            // 2) Compute the timeline frame from X
            //    Use the header control to get X in timeline coords
            double dropX = e.GetPosition(header).X;
            int frame = (int)Math.Round(
                dropX
              / Constants.TimelinePixelsInSeparator
              * Constants.TimelineZooms[Project.TimelineZoom],
              MidpointRounding.ToZero);
            var position = new TimeCode(frame, Project.FPS);

            // 3) Compute Y relative to the top of the first track
            //    We get the drop point in the RootCanvas, then subtract header height
            Point canvasPoint = e.GetPosition(RootCanvas);
            double yInTracks = canvasPoint.Y - header.ActualHeight;
            if (yInTracks < 0) yInTracks = 0;

            // 4) Find which track row this Y falls into
            Track targetTrack = null;
            double cumulative = 0;
            foreach (var t in Project.Tracks)
            {
                cumulative += t.Height;
                if (yInTracks <= cumulative)
                {
                    targetTrack = t;
                    break;
                }
            }
            if (targetTrack == null)
                return;

            // 5) Only proceed if the clip is compatible with that track type
            if (!clip.IsCompatibleWith(targetTrack.Type))
                return;

            // 6) Create the correct ITrackItem
            ITrackItem ti;
            if (clip.TrackType == TimelineTrackType.Audio)
            {
                ti = new AudioTrackItem(
                        clip,
                        position,
                        new TimeCode(0, clip.FPS),
                        clip.Length);
            }
            else
            {
                ti = new TrackItem(
                        clip,
                        position,
                        new TimeCode(0, clip.FPS),
                        clip.Length);
            }

            // 7) Add it to that track
            targetTrack.AddTrackItem(ti);

            // 8) Optionally, update project resolution if this is a video
            if (clip.TrackType == TimelineTrackType.Video)
                Project.SetProjectResolution(clip.Width, clip.Height);

            e.Handled = true;
        }


        private Track AddAudioTrackAtBottom()
        {
            var newTrack = new Track
            {
                Name = "Audio",
                Type = TimelineTrackType.Audio
            };
            Project.Tracks.Add(newTrack);
            return newTrack;
        }

        private void Timeline_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(ProjectClip)) is ProjectClip clip) || !(e.OriginalSource is FrameworkElement fe && fe.DataContext is ITimelineTrack tt && clip.IsCompatibleWith(tt.Type)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
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
    }
}