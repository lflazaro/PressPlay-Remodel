using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
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
                // Select the clip
                trackItem.IsSelected = true;

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
        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);

            if (DataContext is TrackItem trackItem && !trackItem.IsSelected)
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
    }
}
