using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PressPlay.Helpers;
using PressPlay.Models;
using PressPlay.Utilities;

namespace PressPlay.Timeline
{
    /// <summary>
    /// Interaction logic for TrackItemControl.xaml
    /// </summary>
    public partial class TrackItemControl : Border
    {
        private TimelineControl _timelineControl;
        private Point _startPoint;
        private Keyframe? _draggingKeyframe;
        private ItemsControl? _draggingStrip;

        public TrackItemControl()
        {
            InitializeComponent();

            // Handle clip selection/drag on mouse down (bubbling)
            this.MouseLeftButtonDown += TrackItem_MouseLeftButtonDown;

            // Initialize volume slider value and visibility once loaded
            this.Loaded += TrackItemControl_Loaded;
        }

        private void TrackItemControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TrackItem ti)
            {
                // Ensure default volume is set
                if (ti.Volume <= 0)
                    ti.Volume = 1.0f;

                // Reflect current volume in slider
                volumeSlider.Value = ti.Volume;

                // Show or hide volume control based on track type
                bool isVideo = ti.Type.ToString().Contains("Video");
                VolumeControl.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
                Debug.WriteLine($"Volume control {(isVideo ? "visible" : "hidden")} for {ti.FileName}");
            }
        }

        /// <summary>
        /// Updates the TrackItem's volume when the slider value changes.
        /// </summary>
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && DataContext is TrackItem item)
            {
                float newVolume = (float)slider.Value;
                item.Volume = newVolume;
                Debug.WriteLine($"Volume updated: {newVolume} for clip {item.FileName}");

                // If playback is active, force immediate audio update
                _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);
                if (_timelineControl?.Project?.IsPlaying == true)
                {
                    _timelineControl.Project.RaiseNeedlePositionTimeChanged(
                        _timelineControl.Project.NeedlePositionTime);
                }
            }
        }

        private void TrackItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);

            // If click occurs within the volume control area, do not start clip drag
            if (VolumeControl.Visibility == Visibility.Visible)
            {
                Point pt = e.GetPosition(VolumeControl);
                if (pt.X >= 0 && pt.X <= VolumeControl.ActualWidth &&
                    pt.Y >= 0 && pt.Y <= VolumeControl.ActualHeight)
                {
                    Debug.WriteLine("Click on volume control - skipping clip drag");
                    e.Handled = true;
                    return;
                }
            }

            if (DataContext is TrackItem trackItem)
            {
                // Update selection state
                UpdateSelection(true);

                // Prepare for potential dragging
                _startPoint = e.GetPosition(this);
                this.CaptureMouse();

                e.Handled = true;
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Cursor = Cursors.Hand; // Indicate draggable
        }
        // Add this method to the TrackItemControl class
        private void UpdateSelection(bool isSelected)
        {
            if (DataContext is TrackItem trackItem)
            {
                // Update selection state
                trackItem.IsSelected = isSelected;

                // Get the app-wide view model instance
                var viewModel = MainWindowViewModel.Instance;

                // Clear other selections if not multi-select
                if (isSelected && Keyboard.Modifiers != ModifierKeys.Control)
                {
                    // Deselect all other items
                    foreach (var track in viewModel.CurrentProject.Tracks)
                    {
                        foreach (var item in track.Items)
                        {
                            if (item != trackItem)
                                item.IsSelected = false;
                        }
                    }
                }

                // Notify the view model about selection change
                viewModel.SelectionChanged();
            }
        }
        public static void ClearAllSelections()
        {
            // Get the app-wide view model instance
            var viewModel = MainWindowViewModel.Instance;

            if (viewModel != null)
            {
                bool hadSelection = false;

                // Deselect all items
                foreach (var track in viewModel.CurrentProject.Tracks)
                {
                    foreach (var item in track.Items)
                    {
                        if (item.IsSelected)
                        {
                            item.IsSelected = false;
                            hadSelection = true;
                        }
                    }
                }

                // Only notify if there was an actual selection change
                if (hadSelection)
                {
                    viewModel.SelectionChanged();
                }
            }
        }
        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);
            TimelineSelectedTool currentTool = TimelineSelectedTool.SelectionTool;
            if (_timelineControl?.Project != null)
            {
                currentTool = _timelineControl.Project.SelectedTool;
            }

            if (DataContext is TrackItem trackItem && !trackItem.IsSelected || DataContext is AudioTrackItem audioTrackItem && !audioTrackItem.IsSelected)
            {
                double x = e.GetPosition(this).X;
                double w = this.ActualWidth;

                // Show resize cursor at edges, hand otherwise
                if (x <= 5)
                {
                    Cursor = Cursors.SizeWE;
                    resizeBorder.BorderThickness = new Thickness(2, 0, 0, 0);
                }
                else if (x >= w - 5)
                {
                    Cursor = Cursors.SizeWE;
                    resizeBorder.BorderThickness = new Thickness(0, 0, 2, 0);
                }
                else if (currentTool == TimelineSelectedTool.RazorCutTool)
                {
                    Cursor = Cursors.IBeam;
                    resizeBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    Cursor = Cursors.Hand;
                    resizeBorder.BorderThickness = new Thickness(0);
                }
            }

            base.OnPreviewMouseMove(e);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (this.IsMouseCaptured)
                this.ReleaseMouseCapture();

            base.OnPreviewMouseLeftButtonUp(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            resizeBorder.BorderThickness = new Thickness(0);
            base.OnMouseLeave(e);
        }

        private void CutItem_Click(object sender, RoutedEventArgs e)
        {
            _timelineControl?.Project.Cut();
        }

        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            _timelineControl?.Project.Copy();
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is TrackItem)
                _timelineControl?.Project.DeleteSelectedItems();
        }

        private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is AudioTrackItem trackItem)
            {
                var project = MainWindowViewModel.Instance.CurrentProject;
                var offset = PixelCalculator.GetPixels(trackItem.Start.TotalFrames, project.TimelineZoom);
                img.Margin = new Thickness(-offset, 0, 0, 0);
            }
        }

        private void KeyframeStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ItemsControl strip && DataContext is TrackItem item)
            {
                if (!item.KeyframesEnabled) return;

                _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);
                var project = _timelineControl?.Project;
                if (project == null || project.IsPlaying) return;

                if (e.OriginalSource is not DependencyObject originalSource)
                {
                    return;
                }

                var originatingStrip = VisualHelper.GetAncestor<ItemsControl>(originalSource);
                if (!ReferenceEquals(originatingStrip, strip) && !ReferenceEquals(originalSource, strip))
                {
                    return;
                }

                var position = e.GetPosition(strip);
                if (position.X < 0 || position.X > strip.ActualWidth ||
                    position.Y < 0 || position.Y > strip.ActualHeight)
                {
                    return;
                }

                if (e.OriginalSource is Canvas)
                {
                    double x = position.X;
                    int frame = item.Position.TotalFrames + Constants.PixelsToFrames(x, project.TimelineZoom);
                    string property = strip.Tag as string ?? string.Empty;
                    if (!item.Keyframes.TryGetValue(property, out var list)) return;
                    if (!list.Any(k => k.Frame == frame))
                    {
                        double value = item.GetAnimated(property, frame);
                        list.Add(new Keyframe { Frame = frame, Value = value });
                        RefreshKeyframeStrip(property);
                    }
                }
            }
        }

        private void Keyframe_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Keyframe kf)
            {
                _draggingKeyframe = kf;
                _draggingStrip = GetParentItemsControl(fe);
                fe.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Keyframe_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingKeyframe != null && _draggingStrip != null && DataContext is TrackItem item)
            {
                _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);
                var project = _timelineControl?.Project;
                if (project == null) return;

                double x = e.GetPosition(_draggingStrip).X;
                int frame = item.Position.TotalFrames + Constants.PixelsToFrames(x, project.TimelineZoom);
                _draggingKeyframe.Frame = frame;
                RefreshKeyframeStrip(_draggingStrip.Tag as string ?? string.Empty);
            }
        }

        private void Keyframe_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
                fe.ReleaseMouseCapture();
            _draggingKeyframe = null;
            _draggingStrip = null;
        }

        private void Keyframe_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Keyframe kf && DataContext is TrackItem item)
            {
                var strip = GetParentItemsControl(fe);
                string property = strip?.Tag as string ?? string.Empty;
                if (item.Keyframes.TryGetValue(property, out var list))
                {
                    list.Remove(kf);
                    item.NotifyKeyframeChange(property);
                    RefreshKeyframeStrip(property);

                    if (list.Count == 0)
                    {
                        switch (property)
                        {
                            case nameof(TrackItem.TranslateX): item.TranslateX = 0; break;
                            case nameof(TrackItem.TranslateY): item.TranslateY = 0; break;
                            case nameof(TrackItem.Rotation): item.Rotation = 0; break;
                            case nameof(TrackItem.ScaleX): item.ScaleX = 1; break;
                            case nameof(TrackItem.ScaleY): item.ScaleY = 1; break;
                            case nameof(TrackItem.Opacity): item.Opacity = 1; break;
                        }
                    }
                    else
                    {
                        _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);
                        int frame = _timelineControl?.Project?.NeedlePositionTime.TotalFrames ?? 0;
                        item.EvaluateKeyframes(frame);
                    }
                }
                e.Handled = true;
            }
        }

        private ItemsControl? GetParentItemsControl(DependencyObject child)
        {
            while (child != null && child is not ItemsControl)
            {
                child = VisualTreeHelper.GetParent(child);
            }
            return child as ItemsControl;
        }

        private void RefreshKeyframeStrip(string property)
        {
            ItemsControl? strip = property switch
            {
                nameof(TrackItem.TranslateX) => translateXStrip,
                nameof(TrackItem.TranslateY) => translateYStrip,
                nameof(TrackItem.Rotation) => rotationStrip,
                nameof(TrackItem.ScaleX) => scaleXStrip,
                nameof(TrackItem.ScaleY) => scaleYStrip,
                nameof(TrackItem.Opacity) => opacityStrip,
                _ => null
            };

            strip?.Items.Refresh();
        }
    }
}
