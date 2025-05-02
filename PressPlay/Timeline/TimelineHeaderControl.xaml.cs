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

            double totalFrames = Project.GetTotalFrames();
            double totalSeconds = totalFrames / Project.FPS;
            double pxPerSec = Constants.TimelinePixelsInSeparator
                                 / Constants.TimelineZooms[Project.TimelineZoom];

            int secCount = (int)Math.Ceiling(totalSeconds);
            for (int s = 0; s <= secCount; s++)
            {
                double x = s * pxPerSec;

                // tick
                var line = new Line
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = 0,
                    Y2 = 8,
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(line, x);
                Canvas.SetTop(line, 0);
                RootCanvas.Children.Add(line);

                // label
                var txt = new TextBlock
                {
                    Text = TimeSpan.FromSeconds(s).ToString(@"mm\:ss"),
                    FontSize = 10,
                    Foreground = Brushes.White
                };
                Canvas.SetLeft(txt, x + 2);
                Canvas.SetTop(txt, 10);
                RootCanvas.Children.Add(txt);
            }

            // update scrollable width
            RootCanvas.Width = totalSeconds * pxPerSec;
        }
    }
}
