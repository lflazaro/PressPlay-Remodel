using System;
using System.Diagnostics;
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
            // Add mouse down handler at the Border level
            this.PreviewMouseLeftButtonDown += TrackItem_PreviewMouseLeftButtonDown;
            volumeSlider.ValueChanged += volumeSlider_ValueChanged;
        }
        private void TrackItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Get the parent timeline control
            _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);

            if (DataContext is TrackItem trackItem)
            {
                // Mark item as selected
                trackItem.IsSelected = true;

                // Store mouse position for potential dragging
                _startPoint = e.GetPosition(this);

                // Important: Capture the mouse to get mouse move events
                this.CaptureMouse();

                // Mark the event as handled to prevent it bubbling up
                e.Handled = true;
            }
        }
        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Cursor = Cursors.Hand; // Show hand cursor by default to indicate draggable
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);

            if (DataContext is TrackItem trackItem && !trackItem.IsSelected)
            {
                double mouseX = e.GetPosition(this).X;
                double width = this.ActualWidth;

                // Show appropriate cursor
                if (mouseX <= 5)
                {
                    Cursor = Cursors.SizeWE; // Left resize
                    resizeBorder.BorderThickness = new Thickness(2, 0, 0, 0);
                }
                else if (mouseX >= width - 5)
                {
                    Cursor = Cursors.SizeWE; // Right resize
                    resizeBorder.BorderThickness = new Thickness(0, 0, 2, 0);
                }
                else
                {
                    Cursor = Cursors.Hand; // Draggable
                    resizeBorder.BorderThickness = new Thickness(0);
                }
            }

            base.OnPreviewMouseMove(e);
        }
        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (this.IsMouseCaptured)
            {
                this.ReleaseMouseCapture();
            }

            base.OnPreviewMouseLeftButtonUp(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            resizeBorder.BorderThickness = new Thickness(0);
            base.OnMouseLeave(e);
        }

        private void CutItem_Click(object sender, RoutedEventArgs e)
        {
            _timelineControl.Project.Cut();
        }

        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            _timelineControl.Project.Copy();
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is TrackItem)
            {
                _timelineControl.Project.DeleteSelectedItems();
            }
        }

        private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is AudioTrackItem trackItem)
            {
                var project = MainWindowViewModel.Instance.CurrentProject;
                var startLength = PixelCalculator.GetPixels(trackItem.Start.TotalFrames, project.TimelineZoom);
                img.Margin = new Thickness(-startLength, 0, 0, 0);
            }
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is TrackItem trackItem)
            {
                trackItem.Volume = (float)e.NewValue;
                Debug.WriteLine($"Volume changed to {trackItem.Volume} for {trackItem.FileName}");
            }
        }
    }
}
