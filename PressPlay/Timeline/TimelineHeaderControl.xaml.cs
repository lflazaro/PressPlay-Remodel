using PressPlay.Helpers;
using PressPlay.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PressPlay.Timeline
{
    public partial class TimelineHeaderControl : UserControl
    {
        public static readonly DependencyProperty ProjectProperty =
            DependencyProperty.Register(
                nameof(Project),
                typeof(Project),
                typeof(TimelineHeaderControl),
                new PropertyMetadata(null, OnProjectChanged));

        public Project Project
        {
            get => (Project)GetValue(ProjectProperty);
            set => SetValue(ProjectProperty, value);
        }

        public TimelineHeaderControl()
        {
            InitializeComponent();

            // Update timeline rendering when scrolling
            HeaderScrollViewer.ScrollChanged += (s, e) =>
            {
                if (e.HorizontalChange != 0)
                {
                    Redraw();
                }
            };
        }

        private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (TimelineHeaderControl)d;

            if (e.OldValue is Project old) ctl.Unwire(old);
            if (e.NewValue is Project @new) ctl.Wire(@new);

            ctl.Redraw();
        }

        void Wire(Project proj)
        {
            proj.PropertyChanged += Project_PropertyChanged;
            proj.Tracks.CollectionChanged += TracksChanged;
            foreach (var t in proj.Tracks)
                t.Items.CollectionChanged += TracksChanged;
        }

        void Unwire(Project proj)
        {
            proj.PropertyChanged -= Project_PropertyChanged;
            proj.Tracks.CollectionChanged -= TracksChanged;
            foreach (var t in proj.Tracks)
                t.Items.CollectionChanged -= TracksChanged;
        }

        private void Project_PropertyChanged(object s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Project.TimelineZoom))
                Redraw();
        }

        private void TracksChanged(object s, NotifyCollectionChangedEventArgs e)
        {
            // hook/unhook new track-items
            if (s is ObservableCollection<Track>)
            {
                if (e.NewItems != null)
                    foreach (Track t in e.NewItems)
                        t.Items.CollectionChanged += TracksChanged;
                if (e.OldItems != null)
                    foreach (Track t in e.OldItems)
                        t.Items.CollectionChanged -= TracksChanged;
            }
            Redraw();
        }

        private void Redraw()
        {
            if (Project == null) return;

            RootCanvas.Children.Clear();

            // Set a very large default timeline duration - 60 minutes
            double minimumTimelineSeconds = 3600; // 60 minutes

            // Get actual total frames from project or use minimum
            double totalFrames = Math.Max(minimumTimelineSeconds * Project.FPS, Project.GetTotalFrames());
            double totalSeconds = totalFrames / Project.FPS;

            // Calculate pixels per frame and per second
            double pixelsPerFrame = Constants.TimelinePixelsInSeparator / Constants.TimelineZooms[Project.TimelineZoom];
            double pixelsPerSecond = pixelsPerFrame * Project.FPS;

            // Determine appropriate label interval based on zoom
            int labelIntervalSeconds = 1;
            if (pixelsPerSecond < 30) labelIntervalSeconds = 5;
            if (pixelsPerSecond < 15) labelIntervalSeconds = 10;
            if (pixelsPerSecond < 8) labelIntervalSeconds = 30;
            if (pixelsPerSecond < 4) labelIntervalSeconds = 60;
            if (pixelsPerSecond < 2) labelIntervalSeconds = 300; // 5 min

            // Set canvas width to a very large value
            double canvasWidth = totalSeconds * pixelsPerSecond;
            RootCanvas.Width = Math.Max(10000, canvasWidth);

            // Only draw timestamps within a reasonable range to prevent performance issues
            // Calculate visible range based on scroll position and viewport width
            double visibleStart = HeaderScrollViewer.HorizontalOffset;
            double visibleEnd = visibleStart + HeaderScrollViewer.ViewportWidth + 500; // Add a buffer

            // Draw timestamp labels with proper spacing for visible area
            int startSecond = Math.Max(0, (int)(visibleStart / pixelsPerSecond) - labelIntervalSeconds);
            int endSecond = (int)(visibleEnd / pixelsPerSecond) + labelIntervalSeconds;

            // Adjust for extremely zoomed out views
            endSecond = Math.Min(endSecond, (int)totalSeconds);

            for (int s = startSecond; s <= endSecond; s += labelIntervalSeconds)
            {
                double x = s * pixelsPerSecond;

                // Draw tick mark
                var line = new Line
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = 0,
                    Y2 = s % (labelIntervalSeconds * 5) == 0 ? 12 : 8, // Longer ticks for major intervals
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(line, x);
                Canvas.SetTop(line, 0);
                RootCanvas.Children.Add(line);

                // Add text label for major intervals only
                if (s % (labelIntervalSeconds * 5) == 0)
                {
                    TimeSpan time = TimeSpan.FromSeconds(s);
                    string timeText = time.TotalHours >= 1
                        ? time.ToString(@"h\:mm\:ss")
                        : time.ToString(@"mm\:ss");

                    var txt = new TextBlock
                    {
                        Text = timeText,
                        FontSize = 10,
                        Foreground = Brushes.White
                    };
                    Canvas.SetLeft(txt, x + 2);
                    Canvas.SetTop(txt, 14);
                    RootCanvas.Children.Add(txt);
                }
            }
        }
    }
}
